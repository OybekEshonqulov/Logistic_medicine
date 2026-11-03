using System.Globalization;
using FluentValidation.Resources;
using PharmaTrack.Domain.Entities;

namespace PharmaTrack.Application.Common;

public static class Languages
{
    public const string UzbekLatin = "uz";
    public const string UzbekCyrillic = "uz-Cyrl";
    public const string Russian = "ru";

    /// <summary>The text for the request's language. A missing translation falls back to Uzbek (Latin).</summary>
    public static string Pick(string uz, string? ru, string? cyrl)
    {
        var chosen = CultureInfo.CurrentUICulture.Name switch
        {
            UzbekCyrillic => cyrl,
            Russian => ru,
            _ => uz
        };
        return string.IsNullOrWhiteSpace(chosen) ? uz : chosen;
    }
}

public sealed record LocalizedText(string Uz, string Ru, string Cyrl)
{
    public string Current => Languages.Pick(Uz, Ru, Cyrl);
}

/// <summary>FluentValidation names its Uzbek translations differently from the cultures this API accepts.</summary>
internal sealed class ValidationLanguageManager : LanguageManager
{
    private static readonly CultureInfo Latin = new("uz-Latn-UZ");
    private static readonly CultureInfo Cyrillic = new("uz-Cyrl-UZ");

    public override string GetString(string key, CultureInfo? culture = null)
    {
        var requested = culture ?? CultureInfo.CurrentUICulture;
        return base.GetString(key, requested.Name switch
        {
            Languages.UzbekLatin => Latin,
            Languages.UzbekCyrillic => Cyrillic,
            _ => requested
        });
    }
}

/// <summary>Every sentence the server writes itself, in all interface languages.</summary>
public static class Texts
{
    public static readonly LocalizedText CentralStore = new("Markaziy ombor", "Центральный склад", "Марказий омбор");
    public static readonly LocalizedText NoDistrict = new("Tuman ko'rsatilmagan", "Район не указан", "Туман кўрсатилмаган");
    public static readonly LocalizedText DuplicateProduct = new("Bir dori bir marta kiritilishi kerak", "Одно лекарство можно указать только один раз", "Бир дори бир марта киритилиши керак");
    public static readonly LocalizedText DuplicateBatch = new("Bir partiya bir marta kiritilishi kerak", "Одну партию можно указать только один раз", "Бир партия бир марта киритилиши керак");

    public static readonly LocalizedText ImportProductNotFound = new("Dori topilmadi yoki bir xil nomli dorilar bor", "Лекарство не найдено или есть лекарства с одинаковым названием", "Дори топилмади ёки бир хил номли дорилар бор");
    public static readonly LocalizedText ImportBatchEmpty = new("Partiya raqami bo'sh", "Номер партии пуст", "Партия рақами бўш");
    public static readonly LocalizedText ImportExpiryInvalid = new("Muddat noto'g'ri", "Неверный срок годности", "Муддат нотўғри");
    public static readonly LocalizedText ImportQuantityInvalid = new("Miqdor noto'g'ri", "Неверное количество", "Миқдор нотўғри");
    public static readonly LocalizedText ImportPriceInvalid = new("Narx noto'g'ri", "Неверная цена", "Нарх нотўғри");
    public static readonly LocalizedText ImportFileEmpty = new("Fayl bo'sh", "Файл пуст", "Файл бўш");

    public static readonly LocalizedText ImportSheet = new("Kirim", "Приход", "Кирим");
    public static readonly LocalizedText[] ImportColumns =
    [
        new("GTIN yoki dori nomi", "GTIN или название лекарства", "GTIN ёки дори номи"),
        new("Partiya raqami", "Номер партии", "Партия рақами"),
        new("Yaroqlilik muddati (YYYY-MM-DD)", "Срок годности (ГГГГ-ММ-ДД)", "Яроқлилик муддати (YYYY-MM-DD)"),
        Quantity,
        new("Narx", "Цена", "Нарх")
    ];

    public static readonly LocalizedText SalesSheet = new("Sotuvlar", "Продажи", "Сотувлар");
    public static readonly LocalizedText StockSheet = new("Qoldiq", "Остаток", "Қолдиқ");
    public static readonly LocalizedText ExpiringSheet = new("Muddati yaqin", "Истекающий срок", "Муддати яқин");
    public static readonly LocalizedText DealersSheet = new("Dilerlar", "Дилеры", "Дилерлар");

    public static readonly LocalizedText[] SalesColumns = [Dealer, Quantity, Amount];
    public static readonly LocalizedText[] StockColumns = [new("Dori / partiya", "Лекарство / партия", "Дори / партия"), Quantity, Value];
    public static readonly LocalizedText[] ExpiringColumns =
    [
        new("Joylashuv", "Местоположение", "Жойлашув"),
        new("Dori", "Лекарство", "Дори"),
        new("Partiya", "Партия", "Партия"),
        new("Muddat", "Срок", "Муддат"),
        new("Qolgan kun", "Осталось дней", "Қолган кун"),
        Quantity,
        Value
    ];
    public static readonly LocalizedText[] DealersColumns =
    [
        Dealer,
        new("Hudud", "Область", "Ҳудуд"),
        new("Sotuv summasi", "Сумма продаж", "Сотув суммаси"),
        new("Sotuv miqdori", "Количество продаж", "Сотув миқдори"),
        new("Qoldiq", "Остаток", "Қолдиқ"),
        new("Zaxira (kun)", "Запас (дней)", "Захира (кун)"),
        new("Oxirgi sotuv", "Последняя продажа", "Охирги сотув")
    ];

    private static LocalizedText Dealer => new("Diler", "Дилер", "Дилер");
    private static LocalizedText Quantity => new("Miqdor", "Количество", "Миқдор");
    private static LocalizedText Amount => new("Summa", "Сумма", "Сумма");
    private static LocalizedText Value => new("Qiymat", "Стоимость", "Қиймат");

    public static LocalizedText LocationText(Guid locationId, string name) =>
        locationId == StockLocation.CentralId ? CentralStore : new LocalizedText(name, name, name);

    public static string LocationName(Guid locationId, string name) => LocationText(locationId, name).Current;

    public static IReadOnlyList<string> Current(IEnumerable<LocalizedText> texts) => texts.Select(t => t.Current).ToList();

    public static LocalizedText DealerSilent(string dealer, int days) => new(
        $"{dealer}: {days} kundan beri sotuv kiritmagan",
        $"{dealer}: нет продаж уже {days} дн.",
        $"{dealer}: {days} кундан бери сотув киритмаган");

    public static LocalizedText DealerNeverReported(string dealer) => new(
        $"{dealer}: hali sotuv kiritmagan",
        $"{dealer}: ещё не вводил продажи",
        $"{dealer}: ҳали сотув киритмаган");

    public static LocalizedText ExpiredStock(LocalizedText location, int units) => new(
        $"{location.Uz}: {units} dona muddati o'tgan dori",
        $"{location.Ru}: просроченных лекарств {units} шт.",
        $"{location.Cyrl}: {units} дона муддати ўтган дори");

    public static LocalizedText ProductStockout(string product) => new(
        $"{product}: qoldiq tugagan",
        $"{product}: остаток закончился",
        $"{product}: қолдиқ тугаган");

    public static LocalizedText ShipmentNotReceived(long number, LocalizedText recipient) => new(
        $"#{number} jo'natma {recipient.Uz} tomonidan qabul qilinmagan",
        $"Отправка #{number} не принята получателем: {recipient.Ru}",
        $"#{number} жўнатма {recipient.Cyrl} томонидан қабул қилинмаган");

    public static LocalizedText OpenDiscrepancy(long number) => new(
        $"#{number} jo'natmada hal qilinmagan farq bor",
        $"В отправке #{number} есть нерешённое расхождение",
        $"#{number} жўнатмада ҳал қилинмаган фарқ бор");

    public static LocalizedText PendingAdjustments(int count) => new(
        $"{count} ta hisobdan chiqarish so'rovi kutmoqda",
        $"Запросов на списание в ожидании: {count}",
        $"{count} та ҳисобдан чиқариш сўрови кутмоқда");

    public static LocalizedText PendingPharmacies(int count) => new(
        $"{count} ta yangi dorixona tasdiq kutmoqda",
        $"Новых аптек ожидает подтверждения: {count}",
        $"{count} та янги дорихона тасдиқ кутмоқда");

    public static LocalizedText BatchExpired(LocalizedText location, string product, string batch, int units) => new(
        $"{location.Uz}: {product} ({batch}) muddati o'tgan, {units} dona",
        $"{location.Ru}: {product} ({batch}) срок годности истёк, {units} шт.",
        $"{location.Cyrl}: {product} ({batch}) муддати ўтган, {units} дона");

    public static LocalizedText BatchExpiring(LocalizedText location, string product, string batch, int days, int units) => new(
        $"{location.Uz}: {product} ({batch}) muddati {days} kundan keyin tugaydi, {units} dona",
        $"{location.Ru}: {product} ({batch}) срок годности истекает через {days} дн., {units} шт.",
        $"{location.Cyrl}: {product} ({batch}) муддати {days} кундан кейин тугайди, {units} дона");

    public static LocalizedText LowStock(string dealer, string product, int units, decimal? days) => new(
        $"{dealer}: {product} qoldig'i {units} dona, taxminan {days} kunga yetadi",
        $"{dealer}: остаток {product} {units} шт., хватит примерно на {days} дн.",
        $"{dealer}: {product} қолдиғи {units} дона, тахминан {days} кунга етади");

    public static LocalizedText SalesNotReported(string dealer) => new(
        $"{dealer}: sotuv ma'lumoti kiritilmagan",
        $"{dealer}: данные о продажах не введены",
        $"{dealer}: сотув маълумоти киритилмаган");
}
