using isp_report_api.Models;
using isp_report_api.Repository;

namespace isp_report_api.Services;

public interface IIspReportService
{
    Task<IEnumerable<IspMonthlyReport>> GetMonthlyReportsAsync(IspReportFilter filter);
    Task<IEnumerable<IspMonthlyReportSeries>> GetMonthlyReportsAllIspsAsync(IspReportFilter filter);
    Task<IEnumerable<string>> GetAllIspNamesAsync();
    Task<IEnumerable<IspStat>> GetPrepaidRetailerDistributionAsync(IspReportFilter filter);
    Task<PrepaidStats> GetPrepaidStatsAsync(IspReportFilter filter);
    Task<IEnumerable<PostpaidReport>> GetPostpaidReportsAsync(IspReportFilter filter);
    Task<IEnumerable<PostpaidReportSeries>> GetPostpaidReportsAllIspsAsync(IspReportFilter filter);
    Task<IEnumerable<string>> GetAllPostpaidIspNamesAsync();
    Task<PostpaidStats> GetPostpaidStatsAsync(IspReportFilter filter);
    Task<IEnumerable<TrafficReport>> GetTrafficDailyAsync(IspReportFilter filter);
    Task<IEnumerable<TrafficReportSeries>> GetTrafficDailyAllIspsAsync(IspReportFilter filter);
    Task<IEnumerable<ProductWeeklySales>> GetProductWeeklySalesAsync();
    Task<IEnumerable<ProductWeeklyBreakdown>> GetProductWeeklyBreakdownAsync(string ispName);
    Task<IEnumerable<Product>> GetProductsAsync();
}

public class IspReportService : IIspReportService
{
    private readonly IIspReportRepository _repository;
    private readonly IProductSalesRepository _productSalesRepository;
    private readonly ICacheService _cacheService;
    private readonly ILogger<IspReportService> _logger;

    public IspReportService(
        IIspReportRepository repository,
        IProductSalesRepository productSalesRepository,
        ICacheService cacheService,
        ILogger<IspReportService> logger
    )
    {
        _repository = repository;
        _productSalesRepository = productSalesRepository;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<IEnumerable<IspMonthlyReport>> GetMonthlyReportsAsync(IspReportFilter filter)
    {
        var cacheKey = _cacheService.GenerateCacheKey("monthly_reports", filter);
        var cached = await _cacheService.GetAsync<List<IspMonthlyReport>>(cacheKey);
        
        if (cached != null)
        {
            _logger.LogDebug("Returning cached monthly reports");
            return cached;
        }

        var result = await _repository.GetMonthlyReportsAsync(filter);
        var resultList = result.ToList();
        await _cacheService.SetAsync(cacheKey, resultList);
        
        return resultList;
    }

    public async Task<IEnumerable<IspMonthlyReportSeries>> GetMonthlyReportsAllIspsAsync(
        IspReportFilter filter
    )
    {
        var cacheKey = _cacheService.GenerateCacheKey("monthly_reports_all_isps", filter);
        var cached = await _cacheService.GetAsync<List<IspMonthlyReportSeries>>(cacheKey);
        
        if (cached != null)
        {
            _logger.LogDebug("Returning cached monthly reports for all ISPs");
            return cached;
        }

        var result = await _repository.GetMonthlyReportsAllIspsAsync(filter);
        var resultList = result.ToList();
        await _cacheService.SetAsync(cacheKey, resultList);
        
        return resultList;
    }

    public async Task<IEnumerable<string>> GetAllIspNamesAsync()
    {
        var cacheKey = "isp_names";
        var cached = await _cacheService.GetAsync<List<string>>(cacheKey);
        
        if (cached != null)
        {
            _logger.LogDebug("Returning cached ISP names");
            return cached;
        }

        var result = await _repository.GetAllIspNamesAsync();
        var resultList = result.ToList();
        // Cache ISP names for longer (6 hours) as they change less frequently
        await _cacheService.SetAsync(cacheKey, resultList, TimeSpan.FromHours(6));
        
        return resultList;
    }

    public async Task<IEnumerable<IspStat>> GetPrepaidRetailerDistributionAsync(
        IspReportFilter filter
    )
    {
        var cacheKey = _cacheService.GenerateCacheKey("prepaid_retailer_distribution", filter);
        var cached = await _cacheService.GetAsync<List<IspStat>>(cacheKey);

        if (cached != null)
        {
            _logger.LogDebug("Returning cached prepaid retailer distribution");
            return cached;
        }

        var result = await _repository.GetPrepaidRetailerDistributionAsync(filter);
        var resultList = result.ToList();
        await _cacheService.SetAsync(cacheKey, resultList);
        return resultList;
    }

    public async Task<PrepaidStats> GetPrepaidStatsAsync(IspReportFilter filter)
    {
        var cacheKey = _cacheService.GenerateCacheKey("prepaid_stats", filter);
        var cached = await _cacheService.GetAsync<PrepaidStats>(cacheKey);
        
        if (cached != null)
        {
            _logger.LogDebug("Returning cached prepaid stats");
            return cached;
        }

        var result = await _repository.GetPrepaidStatsAsync(filter);
        await _cacheService.SetAsync(cacheKey, result);
        
        return result;
    }

    public async Task<IEnumerable<PostpaidReport>> GetPostpaidReportsAsync(IspReportFilter filter)
    {
        var cacheKey = _cacheService.GenerateCacheKey("postpaid_reports", filter);
        var cached = await _cacheService.GetAsync<List<PostpaidReport>>(cacheKey);
        
        if (cached != null)
        {
            _logger.LogDebug("Returning cached postpaid reports");
            return cached;
        }

        var result = await _repository.GetPostpaidReportsAsync(filter);
        var resultList = result.ToList();
        await _cacheService.SetAsync(cacheKey, resultList);
        
        return resultList;
    }

    public async Task<IEnumerable<PostpaidReportSeries>> GetPostpaidReportsAllIspsAsync(
        IspReportFilter filter
    )
    {
        var cacheKey = _cacheService.GenerateCacheKey("postpaid_reports_all_isps", filter);
        var cached = await _cacheService.GetAsync<List<PostpaidReportSeries>>(cacheKey);
        
        if (cached != null)
        {
            _logger.LogDebug("Returning cached postpaid reports for all ISPs");
            return cached;
        }

        var result = await _repository.GetPostpaidReportsAllIspsAsync(filter);
        var resultList = result.ToList();
        await _cacheService.SetAsync(cacheKey, resultList);
        
        return resultList;
    }

    public async Task<IEnumerable<string>> GetAllPostpaidIspNamesAsync()
    {
        var cacheKey = "postpaid_isp_names";
        var cached = await _cacheService.GetAsync<List<string>>(cacheKey);
        
        if (cached != null)
        {
            _logger.LogDebug("Returning cached postpaid ISP names");
            return cached;
        }

        var result = await _repository.GetAllPostpaidIspNamesAsync();
        var resultList = result.ToList();
        // Cache ISP names for longer (6 hours) as they change less frequently
        await _cacheService.SetAsync(cacheKey, resultList, TimeSpan.FromHours(6));
        
        return resultList;
    }

    public async Task<PostpaidStats> GetPostpaidStatsAsync(IspReportFilter filter)
    {
        var cacheKey = _cacheService.GenerateCacheKey("postpaid_stats", filter);
        var cached = await _cacheService.GetAsync<PostpaidStats>(cacheKey);
        
        if (cached != null)
        {
            _logger.LogDebug("Returning cached postpaid stats");
            return cached;
        }

        var result = await _repository.GetPostpaidStatsAsync(filter);
        await _cacheService.SetAsync(cacheKey, result);
        
        return result;
    }

    public async Task<IEnumerable<TrafficReport>> GetTrafficDailyAsync(IspReportFilter filter)
    {
        var cacheKey = _cacheService.GenerateCacheKey("traffic_daily", filter);
        var cached = await _cacheService.GetAsync<List<TrafficReport>>(cacheKey);

        if (cached != null)
        {
            _logger.LogDebug("Returning cached daily traffic reports");
            return cached;
        }

        var result = await _repository.GetTrafficDailyAsync(filter);
        var resultList = result.ToList();
        await _cacheService.SetAsync(cacheKey, resultList);

        return resultList;
    }

    public async Task<IEnumerable<TrafficReportSeries>> GetTrafficDailyAllIspsAsync(
        IspReportFilter filter
    )
    {
        var cacheKey = _cacheService.GenerateCacheKey("traffic_daily_all_isps", filter);
        var cached = await _cacheService.GetAsync<List<TrafficReportSeries>>(cacheKey);

        if (cached != null)
        {
            _logger.LogDebug("Returning cached daily traffic reports for all ISPs");
            return cached;
        }

        var result = await _repository.GetTrafficDailyAllIspsAsync(filter);
        var resultList = result.ToList();
        await _cacheService.SetAsync(cacheKey, resultList);

        return resultList;
    }

    public async Task<IEnumerable<ProductWeeklySales>> GetProductWeeklySalesAsync()
    {
        const string cacheKey = "product_weekly_sales";
        var cached = await _cacheService.GetAsync<List<ProductWeeklySales>>(cacheKey);

        if (cached != null)
        {
            _logger.LogDebug("Returning cached product weekly sales");
            return cached;
        }

        var result = await _productSalesRepository.GetProductWeeklySalesAsync();
        var resultList = result.ToList();
        await _cacheService.SetAsync(cacheKey, resultList);

        return resultList;
    }

    public async Task<IEnumerable<ProductWeeklyBreakdown>> GetProductWeeklyBreakdownAsync(
        string ispName
    )
    {
        var cacheKey = $"product_weekly_breakdown:{ispName}";
        var cached = await _cacheService.GetAsync<List<ProductWeeklyBreakdown>>(cacheKey);

        if (cached != null)
        {
            _logger.LogDebug("Returning cached product weekly breakdown for {Isp}", ispName);
            return cached;
        }

        var result = await _productSalesRepository.GetProductWeeklyBreakdownAsync(ispName);
        var resultList = result.ToList();
        await _cacheService.SetAsync(cacheKey, resultList);

        return resultList;
    }

    public async Task<IEnumerable<Product>> GetProductsAsync()
    {
        const string cacheKey = "products_list";
        var cached = await _cacheService.GetAsync<List<Product>>(cacheKey);

        if (cached != null)
        {
            _logger.LogDebug("Returning cached products list");
            return cached;
        }

        var result = await _productSalesRepository.GetProductsAsync();
        var resultList = result.ToList();
        await _cacheService.SetAsync(cacheKey, resultList);

        return resultList;
    }
}
