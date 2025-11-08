using Application.IServices;
using Application.Mapping;
using Application.Services;
using Application.Services.SignalR;
using Microsoft.AspNetCore.Mvc;
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

            services.AddScoped<ISetService, SetService>();


            return services;
        }
    }
}
