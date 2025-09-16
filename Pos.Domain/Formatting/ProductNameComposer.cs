// Pos.Domain/Formatting/ProductNameComposer.cs

using System;
using Pos.Domain.Entities;

namespace Pos.Domain.Formatting
{
    public enum VariantStyle
    {
        ValuesOnly,       // "Red / XL"
        NamesAndValues    // "Color: Red / Size: XL"
    }

    public sealed class ProductNameOptions
    {
        public bool PreferProductName { get; init; } = true; // if product exists, use it as base
        public VariantStyle VariantStyle { get; init; } = VariantStyle.ValuesOnly;
        public string VariantJoiner { get; init; } = " / ";
        public string VariantPrefix { get; init; } = " — ";  // em dash prefix before variants
        public bool HideEmptyVariantValues { get; init; } = true;
    }

    public static class ProductNameComposer
    {
        public static string Compose(
            string? productName, string itemName,
            string? v1Name, string? v1Value,
            string? v2Name, string? v2Value,
            ProductNameOptions? options = null)
        {
            options ??= new ProductNameOptions();

            var baseName = options.PreferProductName && !string.IsNullOrWhiteSpace(productName)
                ? productName!
                : itemName;

            var v1 = BuildVariantPart(v1Name, v1Value, options);
            var v2 = BuildVariantPart(v2Name, v2Value, options);

            var body = JoinNonEmpty(options.VariantJoiner, v1, v2);
            if (string.IsNullOrWhiteSpace(body)) return baseName;

            return baseName + options.VariantPrefix + body;
        }

        public static string Compose(Item item, Product? product = null, ProductNameOptions? options = null)
            => Compose(product?.Name, item.Name, item.Variant1Name, item.Variant1Value, item.Variant2Name, item.Variant2Value, options);

        private static string? BuildVariantPart(string? name, string? value, ProductNameOptions opt)
        {
            if (string.IsNullOrWhiteSpace(value))
                return opt.HideEmptyVariantValues ? null : value ?? "";

            return opt.VariantStyle == VariantStyle.ValuesOnly
                ? value
                : (!string.IsNullOrWhiteSpace(name) ? $"{name}: {value}" : value);
        }

        private static string JoinNonEmpty(string joiner, params string?[] parts)
        {
            var list = System.Linq.Enumerable.Where(parts, p => !string.IsNullOrWhiteSpace(p));
            return string.Join(joiner, list!);
        }
    }
}
