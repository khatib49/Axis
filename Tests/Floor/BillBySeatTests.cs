using Application.DTOs;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Tests.Floor
{
    public class BillBySeatTests : FloorTestBase
    {
        private void SeedSession(int trxId, int guests, int tableId = 1)
        {
            Db.Add(new TableSession { TableId = tableId, TransactionId = trxId, GuestCount = guests, OpenedBy = "seed", Status = TableSession.StatusOpen });
            Db.SaveChanges();
        }

        [Fact]
        public async Task Bill_by_seat_groups_lines_and_sums_price_times_quantity_plus_stored_extras()
        {
            // 2 burgers (10) + 3 colas (2.5) = 27.50; burgers carry a 1.00 add-on on each unit (2.00 total) → invoice 29.50
            SeedInvoice(20, 7, (1, 2), (2, 3));
            SeedSession(20, 2);
            Db.Add(new ItemAddOn { Id = 1, ItemId = 1, Name = "Cheese", Price = 1m });
            Db.Add(new TransactionItemAddOn { TransactionRecordId = 20, ItemId = 1, AddOnId = 1, Name = "Cheese", UnitPrice = 1m, Quantity = 2 });
            var trx = Db.Set<TransactionRecord>().First(t => t.Id == 20);
            trx.TotalPrice = 29.5m;
            Db.SaveChanges();

            var r = await Billing.SetLinesAsync(20, new SetLinesRequestDto(new()
            {
                new(1, 1, 1),          // seat 1: 1 burger
                new(1, 2, 1, 2),       // seat 2: 1 burger, course 2
                new(2, 1, 2),          // seat 1: 2 colas
            }));
            r.Success.Should().BeTrue(r.Message);
            var bill = r.Data!;
            bill.GuestCount.Should().Be(2);
            bill.Seats.Select(s => s.Label).Should().Equal("Seat 1", "Seat 2", "Table");

            var seat1 = bill.Seats[0];
            seat1.Lines.Should().HaveCount(2);
            seat1.Lines.Single(l => l.ItemId == 1).LineTotal.Should().Be(11m);   // 10 + 1.00 extras per unit
            seat1.Lines.Single(l => l.ItemId == 2).LineTotal.Should().Be(5m);    // 2 × 2.5
            seat1.Subtotal.Should().Be(16m);
            seat1.Net.Should().Be(16m);

            var seat2 = bill.Seats[1];
            seat2.Subtotal.Should().Be(11m);
            seat2.Lines.Single().CourseNumber.Should().Be(2);

            var table = bill.Seats[2];
            table.Lines.Single().Should().Match<SeatLineDto>(l => l.ItemId == 2 && l.Quantity == 1 && l.LineTotal == 2.5m);
            bill.AssignedTotal.Should().Be(27m);
            bill.UnassignedTotal.Should().Be(2.5m);
            (bill.AssignedTotal + bill.UnassignedTotal).Should().Be(bill.TotalPrice);
        }

        [Fact]
        public async Task Append_accumulates_quantities_and_rejects_more_than_the_invoice_line_or_the_guest_count()
        {
            SeedInvoice(21, 7, (1, 2));
            SeedSession(21, 2);

            (await Billing.SetLinesAsync(21, new SetLinesRequestDto(new() { new(1, 1, 1) }))).Success.Should().BeTrue();
            var again = await Billing.SetLinesAsync(21, new SetLinesRequestDto(new() { new(1, 1, 1) }));
            again.Success.Should().BeTrue(again.Message);
            again.Data!.Seats.Single(s => s.SeatNumber == 1).Lines.Single().Quantity.Should().Be(2);

            var tooMany = await Billing.SetLinesAsync(21, new SetLinesRequestDto(new() { new(1, 2, 1) }));
            tooMany.Success.Should().BeFalse();
            tooMany.Message.Should().Contain("exceed");

            var badSeat = await Billing.SetLinesAsync(21, new SetLinesRequestDto(new() { new(1, 3, 1) }));
            badSeat.Success.Should().BeFalse();
            badSeat.Message.Should().Contain("guest count");

            (await Billing.SetLinesAsync(21, new SetLinesRequestDto(new() { new(1, 9, 1) }, Append: false))).Success.Should().BeFalse();
            // Set mode: move one unit from seat 1 to seat 2
            var moved = await Billing.SetLinesAsync(21, new SetLinesRequestDto(new() { new(1, 1, 1), new(1, 2, 1) }, Append: false));
            moved.Success.Should().BeTrue(moved.Message);
            moved.Data!.Seats.Should().HaveCount(2);
            moved.Data.Seats.All(s => s.Subtotal == 10m).Should().BeTrue();
        }

        [Fact]
        public async Task Split_by_seat_creates_one_split_per_seat_with_the_seat_net_amount()
        {
            SeedInvoice(22, 7, (1, 2), (2, 2)); // 25.00
            SeedSession(22, 3);
            await Billing.SetLinesAsync(22, new SetLinesRequestDto(new() { new(1, 1, 1), new(1, 2, 1), new(2, 2, 1) })); // seat1 10, seat2 12.5, table 2.5

            var r = await Billing.CreateSeatSplitsAsync(22);
            r.Success.Should().BeTrue(r.Message);
            r.Data!.Splits.Select(s => (s.Note, s.Amount)).Should().Equal(("Seat 1", 10m), ("Seat 2", 12.5m), ("Table", 2.5m));
            r.Data.SplitTotal.Should().Be(25m);
            r.Data.Remaining.Should().Be(25m);

            // Re-splitting replaces the unpaid splits instead of stacking them.
            var twice = await Billing.CreateSeatSplitsAsync(22);
            twice.Success.Should().BeTrue(twice.Message);
            twice.Data!.Splits.Should().HaveCount(3);

            await Billing.PaySplitAsync(twice.Data.Splits[0].Id, new PaySplitRequestDto("Cash"));
            (await Billing.CreateSeatSplitsAsync(22)).Success.Should().BeFalse("paid splits block a re-split");
        }

        [Fact]
        public async Task Split_by_seat_applies_the_invoice_discount_and_never_exceeds_the_invoice_total()
        {
            SeedInvoice(23, 7, (1, 3)); // 30.00 gross
            SeedSession(23, 3);
            Db.Add(new Discount { Id = 5, Name = "10%", Type = "Percentage", Percentage = 10, IsActive = true });
            var trx = Db.Set<TransactionRecord>().First(t => t.Id == 23);
            trx.DiscountId = 5;
            trx.TotalPrice = 27m;
            Db.SaveChanges();
            await Billing.SetLinesAsync(23, new SetLinesRequestDto(new() { new(1, 1, 1), new(1, 2, 1), new(1, 3, 1) }));

            var bill = (await Billing.GetBillBySeatAsync(23)).Data!;
            bill.DiscountPercentage.Should().Be(10m);
            bill.Seats.Should().OnlyContain(s => s.Subtotal == 10m && s.Net == 9m);

            var r = await Billing.CreateSeatSplitsAsync(23);
            r.Success.Should().BeTrue(r.Message);
            r.Data!.SplitTotal.Should().Be(27m);
        }

        [Fact]
        public async Task Seat_rows_do_not_break_course_listing_and_firing()
        {
            SeedInvoice(24, 7, (1, 2), (2, 1));
            SeedSession(24, 2);
            await Billing.SetLinesAsync(24, new SetLinesRequestDto(new() { new(1, 1, 1, 2), new(1, 2, 1) }));

            var courses = await Billing.ListCoursesAsync(24);
            courses.Should().HaveCount(2);
            courses.Single(c => c.ItemId == 1).CourseNumber.Should().Be(2, "the course is shared by every seat portion of the line");

            var fired = await Billing.FireCourseAsync(24, 2);
            fired.Success.Should().BeTrue(fired.Message);
            (await Billing.GetBillBySeatAsync(24)).Data!.Seats.SelectMany(s => s.Lines).Where(l => l.ItemId == 1).Should().OnlyContain(l => l.FiredOn != null);
        }
    }
}
