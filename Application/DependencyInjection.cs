using Application.IServices;
using Application.Mapping;
using Application.Services;
using Application.Services.HangFire;
using Application.Services.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            services.AddSingleton<DomainMapper>(); // Riok mapper

            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IGameService, GameService>();
            services.AddScoped<ICardService, CardService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<ICoffeeShopOrderService, CoffeeShopOrderService>();
            services.AddScoped<IExpenseService, ExpenseService>();
            services.AddScoped<IGameSessionService, GameSessionService>();
            services.AddScoped<IItemService, ItemService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IPassTypeService, PassTypeService>();
            services.AddScoped<IReceiptService, ReceiptService>();
            services.AddScoped<IRoomService, RoomService>();
            services.AddScoped<ISettingService, SettingService>();
            services.AddScoped<IExpenseService, ExpenseService>();
            services.AddScoped<IExpenseCategoryService, ExpenseCategoryService>();

            services.AddHostedService<SessionJobRehydrator>();
            services.AddScoped<ISessionEndMonitor, SessionEndMonitor>();
            services.AddScoped<IMenuService, MenuService>();

            services.AddScoped<ITransactionRecordService, TransactionRecordService>();
            services.AddScoped<IUserCardService, UserCardService>();
            services.AddScoped<IUsersService, UsersService>();
            services.AddScoped<IStatusService, StatusService>();
            services.AddScoped<IRoleCategoryService, RoleCategoryService>();

            services.AddScoped<ISetService, SetService>();
            services.AddScoped<IDiscountService, DiscountService>();

            services.AddScoped<IProfitService, ProfitService>();

            services.AddScoped<IKitchenService, KitchenService>();


            // Register Loyalty Service
            services.AddScoped<ILoyaltyService, LoyaltyService>();
            services.AddScoped<LoyaltyJobs>();

            services.AddSingleton<AccountingMapper>(); // ⭐ NEW

            // ... your existing service registrations ...

            // ADD THESE LINES:
            services.AddScoped<IAccountService, AccountService>(); // ⭐ NEW
            services.AddScoped<IJournalService, JournalService>();


            return services;
        }
    }
}
