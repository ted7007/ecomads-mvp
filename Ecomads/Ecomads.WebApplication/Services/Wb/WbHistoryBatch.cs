namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbHistoryBatch(DateOnly StartDate, DateOnly EndDate, long[] CampaignIds, bool Retry = false);
