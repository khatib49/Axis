using Application.IServices;
using Application.Mapping;
using Application.Services;
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
            services.AddScoped<ISettingsAttributeService, SettingsAttributeService>();
            services.AddScoped<ISettingsValueService, SettingsValueService>();
            services.AddScoped<ITransactionRecordService, TransactionRecordService>();

            return services;
        }
    }
}
