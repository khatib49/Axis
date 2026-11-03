using Application.IServices;
using Application.Services.Promotions;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Promotions module (gift cards, coupons,
    /// happy hour, combos). Application.DependencyInjection calls this once.
    /// </summary>
    public static class PromotionsModule
    {
        public static IServiceCollection AddPromotionsModule(this IServiceCollection services)
        {
            services.AddScoped<IPromotionInvoiceAdjuster, PromotionInvoiceAdjuster>();
            services.AddScoped<IGiftCardService, GiftCardService>();
            services.AddScoped<ICouponService, CouponService>();
            services.AddScoped<IHappyHourService, HappyHourService>();
            services.AddScoped<IComboService, ComboService>();
            return services;
        }
    }
}
