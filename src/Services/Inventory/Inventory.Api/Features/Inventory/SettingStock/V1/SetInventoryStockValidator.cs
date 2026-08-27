using FluentValidation;

namespace Inventory.Api.Features.Inventory.SettingStock.V1;

internal sealed class SetInventoryStockValidator : AbstractValidator<SetInventoryStockCommand>
{
    public SetInventoryStockValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.OnHand).GreaterThanOrEqualTo(0);
    }
}
