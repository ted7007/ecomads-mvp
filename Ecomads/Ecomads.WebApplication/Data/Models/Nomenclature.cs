using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ecomads.WebApplication.Data.Models;

public class Nomenclature
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string WbNomenclatureId { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid StoreId { get; set; }

    [ForeignKey(nameof(StoreId))]
    public Store Store { get; set; } = null!;
}
