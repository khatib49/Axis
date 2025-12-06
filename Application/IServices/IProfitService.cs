using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.IServices
{
    public interface IProfitService
    {
        /// <summary>
        /// Calculate FNB (Food & Beverage) profit for a given period and optional categories
        /// Excludes TCG Retail items
        /// </summary>
        Task<ProfitDto> CalculateFnbProfitAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default);

        /// <summary>
        /// Calculate Gaming profit for a given period and optional categories (game sessions only)
        /// </summary>
        Task<ProfitDto> CalculateGamingProfitAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default);

        /// <summary>
        /// Calculate TCG Retail profit only
        /// </summary>
        Task<ProfitDto> CalculateTcgRetailProfitAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default);

        /// <summary>
        /// Calculate overall business profit with detailed breakdown
        /// </summary>
        Task<DetailedOverallProfitDto> CalculateOverallProfitAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
    }

}
