using Application.Services;
using Application.Services.Phase4;
using Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Tests.OrderingChannels.OrderingChannelsTestSupport;

namespace Tests.Phase4
{
    public class ItemModel3DServiceTests : IDisposable
    {
        private readonly Infrastructure.Persistence.ApplicationDbContext _db = NewDb();
        private readonly Mock<IMediaStorageService> _media = new();
        private readonly List<string> _deleted = new();
        private readonly ItemModel3DService _svc;
        private int _n;

        public ItemModel3DServiceTests()
        {
            SeedMenu(_db);
            _media.Setup(m => m.SaveAsync(It.IsAny<IFormFile>(), "models", It.IsAny<MediaKind>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IFormFile f, string sub, MediaKind k, CancellationToken _) => $"media/models/{++_n}{Path.GetExtension(f.FileName)}");
            _media.Setup(m => m.DeleteAsync(It.IsAny<string?>())).Callback((string? p) => { if (p != null) _deleted.Add(p); }).Returns(Task.CompletedTask);
            _svc = new ItemModel3DService(_db, _media.Object, User().Object, NullLogger<ItemModel3DService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        private static IFormFile File(string name, byte[] head)
        {
            var bytes = head.Concat(new byte[64]).ToArray();
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "f", name);
        }

        private static IFormFile Glb(string name = "latte.glb") => File(name, "glTF"u8.ToArray());
        private static IFormFile Usdz() => File("latte.usdz", new byte[] { 0x50, 0x4B, 0x03, 0x04 });

        [Fact]
        public async Task First_upload_needs_a_real_glb()
        {
            (await _svc.UploadAsync(1, null, Usdz(), null)).Error.Should().Be("Validation");
            (await _svc.UploadAsync(1, File("fake.glb", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }), null, null)).Error.Should().Be("Validation");
            (await _svc.UploadAsync(1, File("model.obj", "glTF"u8.ToArray()), null, null)).Error.Should().Be("Validation");
            (await _svc.UploadAsync(1, Glb(), File("x.usdz", "glTF"u8.ToArray()), null)).Error.Should().Be("Validation");
            (await _svc.UploadAsync(99, Glb(), null, null)).Error.Should().Be("NotFound");

            var ok = await _svc.UploadAsync(1, Glb(), Usdz(), null);
            ok.Success.Should().BeTrue(ok.Message);
            ok.Data!.ItemName.Should().Be("Latte");
            ok.Data.GlbPath.Should().EndWith(".glb");
            ok.Data.UsdzPath.Should().EndWith(".usdz");
        }

        [Fact]
        public async Task Replacing_a_file_deletes_the_old_one_and_keeps_the_rest()
        {
            var first = (await _svc.UploadAsync(1, Glb(), Usdz(), null)).Data!;
            var second = (await _svc.UploadAsync(1, Glb("v2.glb"), null, null)).Data!;
            second.GlbPath.Should().NotBe(first.GlbPath);
            second.UsdzPath.Should().Be(first.UsdzPath);
            _deleted.Should().Equal(first.GlbPath);
        }

        [Fact]
        public async Task Public_list_hides_inactive_models_and_disabled_items()
        {
            await _svc.UploadAsync(1, Glb(), null, null);
            await _svc.UploadAsync(2, Glb(), null, null);
            await _svc.UploadAsync(3, Glb(), null, null);
            await _svc.SetActiveAsync(2, false);
            var burger = _db.Set<Item>().First(i => i.Id == 3); burger.StatusId = 2; _db.SaveChanges();
            (await _svc.ListPublicAsync()).Select(m => m.ItemId).Should().Equal(1);
            (await _svc.DeleteAsync(1)).Should().BeTrue();
            (await _db.Set<ItemModel3D>().CountAsync()).Should().Be(2);
        }
    }
}
