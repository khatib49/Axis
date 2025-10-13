using Application.DTOs;
using Domain.Entities;
using Domain.Identity;
using Riok.Mapperly.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Application.Mapping
{

    // You can tweak behavior with [Mapper(...)] options later.
    // Examples: AllowNullPropertyAssignment, RequiredMappingStrategy, etc. :contentReference[oaicite:1]{index=1}
    [Mapper]
    public partial class DomainMapper
    {
        // ---------- Identity ----------
        // Note: roles are provided as an additional parameter and bound by name (case-insensitive). :contentReference[oaicite:2]{index=2}
        public partial UserDto ToDto(AppUser user, IList<string> roles);

        // ---------- Game ----------
        public GameDto ToDto(Game e) =>
            new GameDto(
                e.Id,
                e.Name,
                e.CategoryId,
                e.Category?.Name ?? string.Empty,
                e.StatusId,
                e.Status?.Name ?? string.Empty,
                e.CreatedOn,
                e.ModifiedOn ?? null
            );
        public partial Game ToEntity(GameCreateDto dto);
        // Update existing target in-place (nulls ignored unless you configure otherwise). :contentReference[oaicite:3]{index=3}
        public partial void MapTo(GameUpdateDto dto, [MappingTarget] Game e);

        // ---------- Room ----------
        public RoomDto ToDto(Room e) =>
            new RoomDto(
                e.Id,
                e.Name,
                e.CategoryId,
                e.Category?.Name ?? string.Empty,
                e.Sets?.Count ?? 0
            );
        public partial Room ToEntity(RoomCreateDto dto);
        public partial void MapTo(RoomUpdateDto dto, [MappingTarget] Room e);

        // ---------- Card ----------
        public partial CardDto ToDto(Card e);
        public partial Card ToEntity(CardCreateDto dto);
        public partial void MapTo(CardUpdateDto dto, [MappingTarget] Card e);

        // ---------- PassType ----------
        public partial PassTypeDto ToDto(PassType e);
        public partial PassType ToEntity(PassTypeCreateDto dto);
        public partial void MapTo(PassTypeUpdateDto dto, [MappingTarget] PassType e);

        // ---------- GameSession ----------
        public partial GameSessionDto ToDto(GameSession e);
        public partial GameSession ToEntity(GameSessionCreateDto dto);
        public partial void MapTo(GameSessionUpdateDto dto, [MappingTarget] GameSession e);

        // ---------- Transaction ----------
        public TransactionDto ToDto(TransactionRecord e) =>
            new TransactionDto(
                e.Id,
                e.RoomId,
                e.Room?.Name ?? string.Empty,
                e.GameTypeId,
                e.GameType?.Name ?? string.Empty,
                e.GameId,
                e.Game?.Name ?? string.Empty,
                e.GameSettingId,
                e.GameSetting?.Name ?? string.Empty,
                e.Hours,
                e.TotalPrice,
                e.StatusId,
                e.CreatedOn,
                e.ModifiedOn,
                e.CreatedBy,
                e.TransactionItems.Select(ti => new TransactionItemDto(
                    ti.ItemId,
                    ti.Item.Name,
                    ti.Quantity,
                    ti.Item.Price,
                    ti.Item.Type,
                    ti.Item.CoffeeShopOrders.Select(co => new CoffeeShopOrderDto(
                        co.Id,
                        co.UserId,
                        co.CardId,
                        co.ItemId,
                        co.Quantity,
                        co.Price,
                        co.Timestamp
                    )).ToList()
                )).ToList(),
                e.SetId,
                e.Set?.Name ?? string.Empty
            );


        public partial TransactionRecord ToEntity(TransactionCreateDto dto);
        public partial void MapTo(TransactionUpdateDto dto, [MappingTarget] TransactionRecord e);

        // ---------- Receipt ----------
        public partial ReceiptDto ToDto(Receipt e);
        public partial Receipt ToEntity(ReceiptCreateDto dto);
        public partial void MapTo(ReceiptUpdateDto dto, [MappingTarget] Receipt e);

        // ---------- Settings / Attributes / Values ----------
    public SettingDto ToDto(Setting e) =>
        new SettingDto(
            e.Id,
            e.Name,
            e.Type,
            e.GameId,
            e.Game?.Name ?? string.Empty,
            e.Hours,
            e.Price,
            e.CreatedOn,
            e.ModifiedOn,
            e.CreatedBy,
            e.ModifiedBy,
            e.IsOffer
          
        );

        public partial Setting ToEntity(SettingCreateDto dto);
        public partial void MapTo(SettingUpdateDto dto, [MappingTarget] Setting e);

       

      
        // ---------- Category ----------
        public partial CategoryDto ToDto(Category e);
        public partial Category ToEntity(CategoryCreateDto dto);
        public partial void MapTo(CategoryUpdateDto dto, [MappingTarget] Category e);

        // ---------- Item ----------
        public partial ItemDto ToDto(Item e);
        public partial Item ToEntity(ItemCreateDto dto);
        public partial void MapTo(ItemUpdateDto dto, [MappingTarget] Item e);

        // ---------- CoffeeShopOrder ----------
        public CoffeeShopOrderDto ToDto(CoffeeShopOrder e) =>
            new CoffeeShopOrderDto(
                e.Id,
                e.UserId,
                e.CardId,
                e.ItemId,
                e.Quantity,
                e.Price,
                e.Timestamp
            );
        public partial CoffeeShopOrder ToEntity(CoffeeShopOrderCreateDto dto);
        public partial void MapTo(CoffeeShopOrderUpdateDto dto, [MappingTarget] CoffeeShopOrder e);

        // ---------- Expense ----------
        public partial ExpenseDto ToDto(Expense e);
        public partial Expense ToEntity(ExpenseCreateDto dto);
        public partial void MapTo(ExpenseUpdateDto dto, [MappingTarget] Expense e);

        // ---------- Notification ----------
        public partial NotificationDto ToDto(Notification e);
        public partial Notification ToEntity(NotificationCreateDto dto);
        public partial void MapTo(NotificationUpdateDto dto, [MappingTarget] Notification e);
        
        // ---------- UserCard ----------
        public partial UserCardDto ToDto(UserCard e);
        public partial UserCard ToEntity(UserCardCreateDto dto);
        public partial void MapTo(UserCardUpdateDto dto, [MappingTarget] UserCard e);

        // ---------- Status ----------
        public partial StatusDto ToDto(Status e);
        public partial Status ToEntity(StatusCreateDto dto);
        public partial void MapTo(StatusUpdateDto dto, [MappingTarget] Status e);
    }

}
