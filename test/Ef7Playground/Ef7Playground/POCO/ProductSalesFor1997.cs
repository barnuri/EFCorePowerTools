﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Ef7Playground.Models.POCO;

[Keyless]
[Table("Product Sales for 1997", Schema = "dbo")]
public partial class ProductSalesFor1997
{
    [Required]
    [StringLength(15)]
    public string CategoryName { get; set; } = null!;

    [Required]
    [StringLength(40)]
    public string ProductName { get; set; } = null!;

    [Column(TypeName = "money")]
    public decimal? ProductSales { get; set; }
}
