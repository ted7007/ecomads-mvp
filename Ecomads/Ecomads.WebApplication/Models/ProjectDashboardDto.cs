namespace Ecomads.WebApplication.Models;

public record ProjectKpiDto(
    double Spend,
    double Drr,
    int Clicks,
    double Ctr
);

public record ProjectDashboardDto(
    Guid Id,
    string Name,
    ProjectKpiDto Kpi
);
