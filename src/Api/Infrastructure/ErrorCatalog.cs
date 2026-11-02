using System.Globalization;
using PharmaTrack.Domain.Common;

namespace PharmaTrack.Api.Infrastructure;

/// <summary>HTTP status and user-facing text for each error code, in Uzbek and Russian.</summary>
public static class ErrorCatalog
{
    private sealed record Entry(int Status, string Uz, string Ru);

    private static readonly Dictionary<string, Entry> Entries = new()
    {
        [ErrorCodes.InvalidCredentials] = new(401, "Telefon raqam yoki parol noto'g'ri", "Неверный номер телефона или пароль"),
        [ErrorCodes.InvalidRefreshToken] = new(401, "Sessiya tugagan, qaytadan kiring", "Сессия истекла, войдите снова"),
        [ErrorCodes.UserInactive] = new(403, "Foydalanuvchi faol emas", "Пользователь неактивен"),
        [ErrorCodes.WrongPassword] = new(400, "Joriy parol noto'g'ri", "Текущий пароль неверен"),
        [ErrorCodes.Unauthorized] = new(401, "Avval tizimga kiring", "Сначала войдите в систему"),
        [ErrorCodes.Forbidden] = new(403, "Bu amal uchun ruxsat yo'q", "Нет прав на это действие"),
        [ErrorCodes.NotFound] = new(404, "Ma'lumot topilmadi", "Данные не найдены"),
        [ErrorCodes.Conflict] = new(409, "Bu ma'lumot allaqachon mavjud yoki boshqa yozuvlarga bog'langan", "Эти данные уже существуют или связаны с другими записями"),
        [ErrorCodes.ValidationFailed] = new(400, "Kiritilgan ma'lumotlar noto'g'ri", "Введены неверные данные"),
        [ErrorCodes.InternalError] = new(500, "Serverda xatolik yuz berdi", "Произошла ошибка на сервере"),
        [ErrorCodes.TooManyRequests] = new(429, "Juda ko'p urinish, birozdan keyin qayta urining", "Слишком много попыток, повторите позже"),
        [ErrorCodes.IdempotencyKeyRequired] = new(400, "Idempotency-Key sarlavhasi majburiy", "Заголовок Idempotency-Key обязателен"),
        [ErrorCodes.PhoneTaken] = new(409, "Bu telefon raqam band", "Этот номер телефона уже занят"),
        [ErrorCodes.DealerRequired] = new(400, "Diler foydalanuvchisi uchun mavjud dilerni tanlang", "Для пользователя-дилера выберите существующего дилера"),
        [ErrorCodes.DistrictNotInRegion] = new(400, "Tanlangan tuman shu hududga tegishli emas", "Выбранный район не относится к этой области"),
        [ErrorCodes.BatchExpiryMismatch] = new(409, "Bu partiya boshqa yaroqlilik muddati bilan ro'yxatdan o'tgan", "Эта партия зарегистрирована с другим сроком годности"),
        [ErrorCodes.BatchExpired] = new(400, "Partiyaning yaroqlilik muddati o'tgan", "Срок годности партии истёк"),
        [ErrorCodes.InsufficientStock] = new(409, "Qoldiq yetarli emas", "Недостаточно остатка"),
        [ErrorCodes.InvalidShipmentStatus] = new(409, "Jo'natmaning hozirgi holatida bu amal mumkin emas", "Это действие недоступно в текущем статусе отправки"),
        [ErrorCodes.InvalidShipmentRoute] = new(400, "Jo'natma markaz va diler orasida bo'lishi kerak", "Отправка должна быть между центром и дилером"),
        [ErrorCodes.DiscrepancyNotOpen] = new(409, "Bu qatorda ochiq farq yo'q", "В этой строке нет открытого расхождения"),
        [ErrorCodes.ReceiveItemsMismatch] = new(400, "Jo'natmadagi har bir qator uchun miqdor kiriting", "Укажите количество для каждой строки отправки"),
        [ErrorCodes.PharmacyNotAvailable] = new(400, "Bu dorixonaga sotuv kiritib bo'lmaydi", "Нельзя оформить продажу в эту аптеку"),
        [ErrorCodes.PharmacyNotPending] = new(409, "Dorixona tasdiq kutmayapti", "Аптека не ожидает подтверждения"),
        [ErrorCodes.SaleAlreadyReversed] = new(409, "Sotuv allaqachon bekor qilingan", "Продажа уже отменена"),
        [ErrorCodes.SaleDateInFuture] = new(400, "Sotuv sanasi kelajakda bo'lishi mumkin emas", "Дата продажи не может быть в будущем"),
        [ErrorCodes.CountNotInProgress] = new(409, "Inventarizatsiya allaqachon yakunlangan", "Инвентаризация уже завершена"),
        [ErrorCodes.CountItemsMismatch] = new(400, "Har bir partiya uchun sanalgan miqdorni kiriting", "Укажите подсчитанное количество для каждой партии"),
        [ErrorCodes.AdjustmentNotPending] = new(409, "So'rov allaqachon ko'rib chiqilgan", "Запрос уже рассмотрен"),
        [ErrorCodes.ImportFailed] = new(400, "Faylda xatolar bor", "В файле есть ошибки"),
        [ErrorCodes.UnknownReport] = new(404, "Bunday hisobot yo'q", "Такого отчёта нет")
    };

    public static int StatusOf(string code) => Entries.TryGetValue(code, out var entry) ? entry.Status : 400;

    public static string MessageOf(string code)
    {
        if (!Entries.TryGetValue(code, out var entry)) return code;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? entry.Ru : entry.Uz;
    }
}
