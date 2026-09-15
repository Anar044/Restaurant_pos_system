namespace RestaurantNode.Api.Common;

public static class Ids
{
    public static Guid New() => Guid.CreateVersion7();
}
