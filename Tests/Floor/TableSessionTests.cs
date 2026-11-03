using Application.DTOs;
using Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Floor
{
    public class TableSessionTests : FloorTestBase
    {
        [Fact]
        public async Task Only_one_open_session_per_table()
        {
            var first = await Sessions.OpenTableAsync(new OpenTableRequestDto(1, 2));
            first.Success.Should().BeTrue(first.Message);
            first.Data!.Status.Should().Be(TableSession.StatusOpen);

            var again = await Sessions.OpenTableAsync(new OpenTableRequestDto(1, 2));
            again.Success.Should().BeFalse();
            again.Message.Should().Contain("already has an open session");

            SeedInvoice(50, 7, (1, 1));
            var seat = await Sessions.SeatInvoiceAsync(new SeatInvoiceRequestDto(1, 50));
            seat.Success.Should().BeFalse();

            (await Sessions.OpenTableAsync(new OpenTableRequestDto(3, 2))).Success.Should().BeFalse("inactive table");
            (await Sessions.OpenTableAsync(new OpenTableRequestDto(99, 2))).Success.Should().BeFalse("unknown table");
        }

        [Fact]
        public async Task Opening_an_empty_table_creates_an_open_fnb_invoice_with_zero_total()
        {
            var r = await Sessions.OpenTableAsync(new OpenTableRequestDto(1, 4, Notes: "birthday"));
            var trx = await Db.Set<TransactionRecord>().FirstAsync(t => t.Id == r.Data!.TransactionId);
            trx.StatusId.Should().Be(7);
            trx.TotalPrice.Should().Be(0);
            trx.Comment.Should().Contain("T1");

            var layout = await Floor.GetLayoutAsync();
            var t1 = layout.Areas.Single().Tables.Single(t => t.Id == 1);
            t1.OpenSession.Should().NotBeNull();
            t1.OpenSession!.GuestCount.Should().Be(4);
            t1.OpenSession.ItemsCount.Should().Be(0);
            layout.Areas.Single().Tables.Single(t => t.Id == 2).OpenSession.Should().BeNull();
            layout.Areas.Single().Tables.Should().NotContain(t => t.Id == 3, "inactive tables are hidden");
        }

        [Fact]
        public async Task Seating_requires_an_open_fnb_invoice_not_already_seated()
        {
            SeedInvoice(60, 6, (1, 1));
            (await Sessions.SeatInvoiceAsync(new SeatInvoiceRequestDto(1, 60))).Success.Should().BeFalse("closed invoice");

            SeedInvoice(61, 7, (1, 1));
            (await Sessions.SeatInvoiceAsync(new SeatInvoiceRequestDto(1, 61))).Success.Should().BeTrue();
            (await Sessions.SeatInvoiceAsync(new SeatInvoiceRequestDto(2, 61))).Success.Should().BeFalse("already seated at T1");
        }

        [Fact]
        public async Task Transfer_moves_the_invoice_to_a_free_table_only()
        {
            var a = await Sessions.OpenTableAsync(new OpenTableRequestDto(1, 2));
            var b = await Sessions.OpenTableAsync(new OpenTableRequestDto(2, 2));

            (await Sessions.TransferAsync(a.Data!.Id, new TransferSessionRequestDto(2))).Success.Should().BeFalse("T2 is occupied");
            (await Sessions.TransferAsync(a.Data.Id, new TransferSessionRequestDto(1))).Success.Should().BeFalse("same table");
            (await Sessions.TransferAsync(a.Data.Id, new TransferSessionRequestDto(3))).Success.Should().BeFalse("inactive table");

            await Sessions.CloseByTransactionAsync(b.Data!.TransactionId);
            var moved = await Sessions.TransferAsync(a.Data.Id, new TransferSessionRequestDto(2));
            moved.Success.Should().BeTrue(moved.Message);
            moved.Data!.TableId.Should().Be(2);
            moved.Data.TransactionId.Should().Be(a.Data.TransactionId);

            var old = await Db.Set<TableSession>().AsNoTracking().FirstAsync(s => s.Id == a.Data.Id);
            old.Status.Should().Be(TableSession.StatusTransferred);
            old.ClosedOn.Should().NotBeNull();
            (await Sessions.TransferAsync(a.Data.Id, new TransferSessionRequestDto(1))).Success.Should().BeFalse("not open any more");

            // T1 is free again, T2 holds the invoice.
            (await Sessions.OpenTableAsync(new OpenTableRequestDto(1, 1))).Success.Should().BeTrue();
            (await Sessions.ListOpenAsync()).Should().HaveCount(2);
        }

        [Fact]
        public async Task Merge_moves_lines_recomputes_target_and_closes_source()
        {
            SeedInvoice(70, 7, (1, 1), (2, 2));   // burger + 2 cola = 15.00
            SeedInvoice(71, 7, (2, 1), (1, 3));   // cola + 3 burgers = 32.50
            await Sessions.SeatInvoiceAsync(new SeatInvoiceRequestDto(1, 70));
            await Sessions.SeatInvoiceAsync(new SeatInvoiceRequestDto(2, 71));

            var r = await Sessions.MergeAsync(new MergeInvoicesRequestDto(70, 71));
            r.Success.Should().BeTrue(r.Message);
            r.Data!.MovedLines.Should().Be(2);
            r.Data.TargetTotal.Should().Be(47.5m);

            var target = await Db.Set<TransactionRecord>().AsNoTracking().Include(t => t.TransactionItems).FirstAsync(t => t.Id == 71);
            target.TotalPrice.Should().Be(47.5m);
            target.TransactionItems.Single(l => l.ItemId == 1).Quantity.Should().Be(4);
            target.TransactionItems.Single(l => l.ItemId == 2).Quantity.Should().Be(3);

            var source = await Db.Set<TransactionRecord>().AsNoTracking().Include(t => t.TransactionItems).FirstAsync(t => t.Id == 70);
            source.TransactionItems.Should().BeEmpty();
            source.TotalPrice.Should().Be(0);
            source.StatusId.Should().Be(6);
            source.Comment.Should().Contain("Merged into #71");

            var sourceSession = await Db.Set<TableSession>().AsNoTracking().FirstAsync(s => s.TransactionId == 70);
            sourceSession.Status.Should().Be(TableSession.StatusMerged);
            (await Db.Set<TableSession>().AsNoTracking().FirstAsync(s => s.TransactionId == 71)).Status.Should().Be(TableSession.StatusOpen);
            (await Db.Set<TransactionAuditLog>().CountAsync(a => a.Action == "FloorMerge")).Should().Be(2);

            // T1 is released by the merge.
            (await Sessions.OpenTableAsync(new OpenTableRequestDto(1, 1))).Success.Should().BeTrue();
        }

        [Fact]
        public async Task Merge_requires_two_distinct_open_fnb_invoices()
        {
            SeedInvoice(80, 7, (1, 1));
            SeedInvoice(81, 6, (1, 1));
            (await Sessions.MergeAsync(new MergeInvoicesRequestDto(80, 80))).Success.Should().BeFalse("same invoice");
            (await Sessions.MergeAsync(new MergeInvoicesRequestDto(80, 81))).Success.Should().BeFalse("target closed");
            (await Sessions.MergeAsync(new MergeInvoicesRequestDto(81, 80))).Success.Should().BeFalse("source closed");
            (await Sessions.MergeAsync(new MergeInvoicesRequestDto(80, 999))).Success.Should().BeFalse("missing target");
        }

        [Fact]
        public async Task Courses_default_to_one_and_fire_stamps_only_that_course()
        {
            SeedInvoice(90, 7, (1, 1), (2, 1));
            var set = await Billing.SetCoursesAsync(90, new SetCoursesRequestDto(new() { new(2, 2) }));
            set.Success.Should().BeTrue(set.Message);
            set.Data!.Single(c => c.ItemId == 1).CourseNumber.Should().Be(1);
            set.Data.Single(c => c.ItemId == 2).CourseNumber.Should().Be(2);

            (await Billing.SetCoursesAsync(90, new SetCoursesRequestDto(new() { new(99, 1) }))).Success.Should().BeFalse("item not on invoice");

            var fired = await Billing.FireCourseAsync(90, 1);
            fired.Success.Should().BeTrue(fired.Message);
            fired.Data!.Single(c => c.ItemId == 1).FiredOn.Should().NotBeNull();
            fired.Data.Single(c => c.ItemId == 2).FiredOn.Should().BeNull();
            (await Billing.FireCourseAsync(90, 5)).Success.Should().BeFalse("empty course");
        }
    }
}
