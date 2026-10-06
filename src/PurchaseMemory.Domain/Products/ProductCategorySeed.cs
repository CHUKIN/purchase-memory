namespace PurchaseMemory.Domain.Products;

/// <summary>
/// The base tree a household starts with, sized for a Polish grocery basket.
/// Identifiers are fixed so migrations on both providers insert the same rows
/// and so later seeds can refer to them. Never change an existing identifier or
/// key; add new rows with new numbers instead.
/// </summary>
public static class ProductCategorySeed
{
    /// <summary>Fixed moment stamped on the seed rows.</summary>
    public static readonly DateTimeOffset SeededAt = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<ProductCategory> Categories { get; } =
    [
        Seed(1, null, "Молочное и яйца", "dairy", 0),
        Seed(2, 1, "Молоко", "milk", 0),
        Seed(3, 1, "Сыр", "cheese", 1),
        Seed(4, 1, "Йогурты и кефир", "yogurt", 2),
        Seed(5, 1, "Масло и сливки", "butter_cream", 3),
        Seed(6, 1, "Творог", "curd", 4),
        Seed(7, 1, "Яйца", "eggs", 5),
        Seed(8, null, "Мясо и рыба", "meat_fish", 1),
        Seed(9, 8, "Мясо", "meat", 0),
        Seed(10, 8, "Птица", "poultry", 1),
        Seed(11, 8, "Колбасы и сосиски", "sausages", 2),
        Seed(12, 8, "Рыба и морепродукты", "fish", 3),
        Seed(13, null, "Овощи и фрукты", "produce", 2),
        Seed(14, 13, "Овощи", "vegetables", 0),
        Seed(15, 13, "Фрукты", "fruits", 1),
        Seed(16, 13, "Зелень", "herbs", 2),
        Seed(17, null, "Хлеб и выпечка", "bakery", 3),
        Seed(18, 17, "Хлеб", "bread", 0),
        Seed(19, 17, "Выпечка", "pastry", 1),
        Seed(20, null, "Бакалея", "grocery", 4),
        Seed(21, 20, "Макароны", "pasta", 0),
        Seed(22, 20, "Крупы и рис", "grains", 1),
        Seed(23, 20, "Мука и сахар", "flour_sugar", 2),
        Seed(24, 20, "Растительное масло", "oils", 3),
        Seed(25, 20, "Соусы и специи", "sauces_spices", 4),
        Seed(26, 20, "Консервы", "canned", 5),
        Seed(27, 20, "Хлопья и завтраки", "breakfast", 6),
        Seed(28, null, "Сладости и снеки", "sweets_snacks", 5),
        Seed(29, 28, "Шоколад и конфеты", "chocolate", 0),
        Seed(30, 28, "Печенье", "cookies", 1),
        Seed(31, 28, "Чипсы и снеки", "chips", 2),
        Seed(32, 28, "Орехи и сухофрукты", "nuts", 3),
        Seed(33, null, "Напитки", "drinks", 6),
        Seed(34, 33, "Кофе", "coffee", 0),
        Seed(35, 33, "Чай", "tea", 1),
        Seed(36, 33, "Соки", "juices", 2),
        Seed(37, 33, "Вода", "water", 3),
        Seed(38, 33, "Газировка", "soft_drinks", 4),
        Seed(39, 33, "Алкоголь", "alcohol", 5),
        Seed(40, null, "Замороженное", "frozen", 7),
        Seed(41, 40, "Полуфабрикаты", "frozen_meals", 0),
        Seed(42, 40, "Мороженое", "ice_cream", 1),
        Seed(43, 40, "Замороженные овощи", "frozen_vegetables", 2),
        Seed(44, null, "Бытовая химия", "household_chemicals", 8),
        Seed(45, 44, "Стирка", "laundry", 0),
        Seed(46, 44, "Для посуды", "dishwashing", 1),
        Seed(47, 44, "Уборка", "cleaning", 2),
        Seed(48, null, "Гигиена", "hygiene", 9),
        Seed(49, 48, "Уход за телом", "body_care", 0),
        Seed(50, 48, "Уход за зубами", "oral_care", 1),
        Seed(51, 48, "Бумажные изделия", "paper", 2),
        Seed(52, null, "Для дома", "home", 10),
        Seed(53, 52, "Кухня", "kitchen", 0),
        Seed(54, 52, "Хозтовары", "home_goods", 1),
        Seed(55, null, "Прочее", "other", 11),
        Seed(56, 55, "Для животных", "pets", 0),
        Seed(57, 55, "Детское", "baby", 1),
        Seed(58, 55, "Пакеты", "bags", 2),
    ];

    public static Guid IdFor(int number) => new($"00000000-0000-4000-8000-{number:D12}");

    private static ProductCategory Seed(int number, int? parentNumber, string name, string key, int position) =>
        ProductCategory.Seed(
            IdFor(number),
            parentNumber is null ? null : IdFor(parentNumber.Value),
            name,
            key,
            position,
            SeededAt);
}
