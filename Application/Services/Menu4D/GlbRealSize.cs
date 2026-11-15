using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace Application.Services.Menu4D
{
    /// <summary>
    /// Scales a GLB model so its widest side (X or Z, glTF is Y-up) is a real width in metres.
    /// Phone AR viewers (Android Scene Viewer, iPhone Quick Look) show a model at the size stored in
    /// the file, so the size is baked in: a new root node with a uniform scale wraps the scene.
    /// Geometry and textures are untouched.
    /// </summary>
    public static class GlbRealSize
    {
        private const uint JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;

        /// <summary>Width of the model in metres (widest of X and Z), or null when it cannot be read.</summary>
        public static double? Width(byte[] glb)
        {
            var (json, _) = Split(glb);
            if (json is null) return null;
            var b = Bounds(json);
            return b is null ? null : Math.Max(b.Value.Max.X - b.Value.Min.X, b.Value.Max.Z - b.Value.Min.Z);
        }

        /// <summary>Returns a copy scaled to <paramref name="widthMeters"/>, or the original bytes if it cannot be read.</summary>
        public static byte[] FitWidth(byte[] glb, double widthMeters)
        {
            if (widthMeters <= 0) return glb;
            var (json, bin) = Split(glb);
            if (json is null) return glb;
            var b = Bounds(json);
            if (b is null) return glb;
            var width = Math.Max(b.Value.Max.X - b.Value.Min.X, b.Value.Max.Z - b.Value.Min.Z);
            if (width <= 1e-9) return glb;
            var s = widthMeters / width;
            if (Math.Abs(s - 1) < 0.005) return glb;

            var nodes = json["nodes"] as JsonArray ?? new JsonArray();
            json["nodes"] = nodes;
            var sceneIndex = json["scene"]?.GetValue<int>() ?? 0;
            var scene = (json["scenes"] as JsonArray)?[sceneIndex] as JsonObject;
            if (scene is null) return glb;
            var roots = scene["nodes"] as JsonArray ?? new JsonArray();
            var children = new JsonArray();
            foreach (var r in roots) children.Add(r!.GetValue<int>());
            nodes.Add(new JsonObject
            {
                ["name"] = "AxisRealSize",
                ["scale"] = new JsonArray(s, s, s),
                ["children"] = children,
            });
            scene["nodes"] = new JsonArray(nodes.Count - 1);
            return Join(json, bin);
        }

        private static (JsonObject? Json, byte[]? Bin) Split(byte[] glb)
        {
            try
            {
                if (glb.Length < 20 || Encoding.ASCII.GetString(glb, 0, 4) != "glTF") return (null, null);
                var pos = 12;
                JsonObject? json = null; byte[]? bin = null;
                while (pos + 8 <= glb.Length)
                {
                    var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(pos));
                    var type = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(pos + 4));
                    if (len < 0 || pos + 8 + len > glb.Length) return (null, null);
                    if (type == JsonChunk) json = JsonNode.Parse(Encoding.UTF8.GetString(glb, pos + 8, len)) as JsonObject;
                    else if (type == BinChunk && bin is null) bin = glb.AsSpan(pos + 8, len).ToArray();
                    pos += 8 + len;
                }
                return (json, bin);
            }
            catch (Exception) { return (null, null); }
        }

        private static byte[] Join(JsonObject json, byte[]? bin)
        {
            var j = Encoding.UTF8.GetBytes(json.ToJsonString());
            var jPad = (4 - j.Length % 4) % 4;
            var bPad = bin is null ? 0 : (4 - bin.Length % 4) % 4;
            var total = 12 + 8 + j.Length + jPad + (bin is null ? 0 : 8 + bin.Length + bPad);
            var o = new byte[total];
            Encoding.ASCII.GetBytes("glTF").CopyTo(o, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(8), (uint)total);
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(12), (uint)(j.Length + jPad));
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(16), JsonChunk);
            j.CopyTo(o, 20);
            for (var i = 0; i < jPad; i++) o[20 + j.Length + i] = 0x20;
            if (bin is not null)
            {
                var p = 20 + j.Length + jPad;
                BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(p), (uint)(bin.Length + bPad));
                BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(p + 4), BinChunk);
                bin.CopyTo(o, p + 8);
            }
            return o;
        }

        /// <summary>World-space bounds of every mesh in the default scene (accessor min/max through node transforms).</summary>
        private static (Vector3 Min, Vector3 Max)? Bounds(JsonObject json)
        {
            try
            {
                var nodes = json["nodes"] as JsonArray;
                var meshes = json["meshes"] as JsonArray;
                var accessors = json["accessors"] as JsonArray;
                var scene = (json["scenes"] as JsonArray)?[json["scene"]?.GetValue<int>() ?? 0] as JsonObject;
                if (nodes is null || meshes is null || accessors is null || scene?["nodes"] is not JsonArray roots) return null;
                var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue); var any = false;
                var stack = new Stack<(int Node, Matrix4x4 Parent, int Depth)>();
                foreach (var r in roots) stack.Push((r!.GetValue<int>(), Matrix4x4.Identity, 0));
                while (stack.Count > 0)
                {
                    var (ni, parent, depth) = stack.Pop();
                    if (depth > 64 || nodes[ni] is not JsonObject n) continue;
                    var world = Local(n) * parent;
                    if (n["mesh"] is JsonNode m && meshes[m.GetValue<int>()]?["primitives"] is JsonArray prims)
                        foreach (var prim in prims)
                        {
                            if (prim?["attributes"]?["POSITION"] is not JsonNode pa) continue;
                            var acc = accessors[pa.GetValue<int>()];
                            if (acc?["min"] is not JsonArray amin || acc["max"] is not JsonArray amax) continue;
                            var lo = new Vector3(F(amin[0]), F(amin[1]), F(amin[2]));
                            var hi = new Vector3(F(amax[0]), F(amax[1]), F(amax[2]));
                            for (var c = 0; c < 8; c++)
                            {
                                var v = Vector3.Transform(new Vector3((c & 1) == 0 ? lo.X : hi.X, (c & 2) == 0 ? lo.Y : hi.Y, (c & 4) == 0 ? lo.Z : hi.Z), world);
                                min = Vector3.Min(min, v); max = Vector3.Max(max, v); any = true;
                            }
                        }
                    if (n["children"] is JsonArray kids)
                        foreach (var k in kids) stack.Push((k!.GetValue<int>(), world, depth + 1));
                }
                return any ? (min, max) : null;
            }
            catch (Exception) { return null; }
        }

        private static float F(JsonNode? v) => (float)v!.GetValue<double>();

        private static Matrix4x4 Local(JsonObject n)
        {
            if (n["matrix"] is JsonArray m && m.Count == 16)
            {
                // glTF matrices are column-major; System.Numerics uses row vectors (v * M), which is the same memory order.
                return new Matrix4x4(F(m[0]), F(m[1]), F(m[2]), F(m[3]), F(m[4]), F(m[5]), F(m[6]), F(m[7]),
                    F(m[8]), F(m[9]), F(m[10]), F(m[11]), F(m[12]), F(m[13]), F(m[14]), F(m[15]));
            }
            var t = n["translation"] is JsonArray ta ? new Vector3(F(ta[0]), F(ta[1]), F(ta[2])) : Vector3.Zero;
            var r = n["rotation"] is JsonArray ra ? new Quaternion(F(ra[0]), F(ra[1]), F(ra[2]), F(ra[3])) : Quaternion.Identity;
            var s = n["scale"] is JsonArray sa ? new Vector3(F(sa[0]), F(sa[1]), F(sa[2])) : Vector3.One;
            return Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
        }
    }
}
