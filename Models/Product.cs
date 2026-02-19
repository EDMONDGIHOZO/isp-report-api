namespace isp_report_api.Models;

/// <summary>
/// DTO representing a product from the PROD_MAPPING table.
/// </summary>
public class Product
{
    /// <summary>The IPP offer code that links to REPORT_ALL_IPP.</summary>
    public string IppOfferCode { get; set; } = string.Empty;

    /// <summary>The friendly product name.</summary>
    public string ProductName { get; set; } = string.Empty;
}
