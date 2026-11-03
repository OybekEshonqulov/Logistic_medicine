using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;

namespace PharmaTrack.Api.Infrastructure;

/// <summary>HTTP status and user-facing text for each error code, in every interface language.</summary>
public static class ErrorCatalog
{
    private sealed record Entry(int Status, string Uz, string Ru, string Cyrl);

    private static readonly Dictionary<string, Entry> Entries = new()
    {
        [ErrorCodes.InvalidCredentials] = new(401, "Telefon raqam yoki parol noto'g'ri", "Неверный номер телефона или пароль", "Телефон рақам ёки парол нотўғри"),
        [ErrorCodes.InvalidRefreshToken] = new(401, "Sessiya tugagan, qaytadan kiring", "Сессия истекла, войдите снова", "Сессия тугаган, қайтадан киринг"),
        [ErrorCodes.UserInactive] = new(403, "Foydalanuvchi faol emas", "Пользователь неактивен", "Фойдаланувчи фаол эмас"),
        [ErrorCodes.WrongPassword] = new(400, "Joriy parol noto'g'ri", "Текущий пароль неверен", "Жорий парол нотўғри"),
        [ErrorCodes.Unauthorized] = new(401, "Avval tizimga kiring", "Сначала войдите в систему", "Аввал тизимга киринг"),
        [ErrorCodes.Forbidden] = new(403, "Bu amal uchun ruxsat yo'q", "Нет прав на это действие", "Бу амал учун рухсат йўқ"),
        [ErrorCodes.NotFound] = new(404, "Ma'lumot topilmadi", "Данные не найдены", "Маълумот топилмади"),
        [ErrorCodes.Conflict] = new(409, "Bu ma'lumot allaqachon mavjud yoki boshqa yozuvlarga bog'langan", "Эти данные уже существуют или связаны с другими записями", "Бу маълумот аллақачон мавжуд ёки бошқа ёзувларга боғланган"),
        [ErrorCodes.ValidationFailed] = new(400, "Kiritilgan ma'lumotlar noto'g'ri", "Введены неверные данные", "Киритилган маълумотлар нотўғри"),
        [ErrorCodes.InternalError] = new(500, "Serverda xatolik yuz berdi", "Произошла ошибка на сервере", "Серверда хатолик юз берди"),
        [ErrorCodes.TooManyRequests] = new(429, "Juda ko'p urinish, birozdan keyin qayta urining", "Слишком много попыток, повторите позже", "Жуда кўп уриниш, бироздан кейин қайта уриниб кўринг"),
        [ErrorCodes.IdempotencyKeyRequired] = new(400, "Idempotency-Key sarlavhasi majburiy", "Заголовок Idempotency-Key обязателен", "Idempotency-Key сарлавҳаси мажбурий"),
        [ErrorCodes.PhoneTaken] = new(409, "Bu telefon raqam band", "Этот номер телефона уже занят", "Бу телефон рақам банд"),
        [ErrorCodes.DealerRequired] = new(400, "Diler foydalanuvchisi uchun mavjud dilerni tanlang", "Для пользователя-дилера выберите существующего дилера", "Дилер фойдаланувчиси учун мавжуд дилерни танланг"),
        [ErrorCodes.DistrictNotInRegion] = new(400, "Tanlangan tuman shu hududga tegishli emas", "Выбранный район не относится к этой области", "Танланган туман шу ҳудудга тегишли эмас"),
        [ErrorCodes.BatchExpiryMismatch] = new(409, "Bu partiya boshqa yaroqlilik muddati bilan ro'yxatdan o'tgan", "Эта партия зарегистрирована с другим сроком годности", "Бу партия бошқа яроқлилик муддати билан рўйхатдан ўтган"),
        [ErrorCodes.BatchExpired] = new(400, "Partiyaning yaroqlilik muddati o'tgan", "Срок годности партии истёк", "Партиянинг яроқлилик муддати ўтган"),
        [ErrorCodes.InsufficientStock] = new(409, "Qoldiq yetarli emas", "Недостаточно остатка", "Қолдиқ етарли эмас"),
        [ErrorCodes.InvalidShipmentStatus] = new(409, "Jo'natmaning hozirgi holatida bu amal mumkin emas", "Это действие недоступно в текущем статусе отправки", "Жўнатманинг ҳозирги ҳолатида бу амал мумкин эмас"),
        [ErrorCodes.InvalidShipmentRoute] = new(400, "Jo'natma markaz va diler orasida bo'lishi kerak", "Отправка должна быть между центром и дилером", "Жўнатма марказ ва дилер орасида бўлиши керак"),
        [ErrorCodes.DiscrepancyNotOpen] = new(409, "Bu qatorda ochiq farq yo'q", "В этой строке нет открытого расхождения", "Бу қаторда очиқ фарқ йўқ"),
        [ErrorCodes.ReceiveItemsMismatch] = new(400, "Jo'natmadagi har bir qator uchun miqdor kiriting", "Укажите количество для каждой строки отправки", "Жўнатмадаги ҳар бир қатор учун миқдор киритинг"),
        [ErrorCodes.PharmacyNotAvailable] = new(400, "Bu dorixonaga sotuv kiritib bo'lmaydi", "Нельзя оформить продажу в эту аптеку", "Бу дорихонага сотув киритиб бўлмайди"),
        [ErrorCodes.PharmacyNotPending] = new(409, "Dorixona tasdiq kutmayapti", "Аптека не ожидает подтверждения", "Дорихона тасдиқ кутмаяпти"),
        [ErrorCodes.SaleAlreadyReversed] = new(409, "Sotuv allaqachon bekor qilingan", "Продажа уже отменена", "Сотув аллақачон бекор қилинган"),
        [ErrorCodes.SaleDateInFuture] = new(400, "Sotuv sanasi kelajakda bo'lishi mumkin emas", "Дата продажи не может быть в будущем", "Сотув санаси келажакда бўлиши мумкин эмас"),
        [ErrorCodes.CountNotInProgress] = new(409, "Inventarizatsiya allaqachon yakunlangan", "Инвентаризация уже завершена", "Инвентаризация аллақачон якунланган"),
        [ErrorCodes.CountItemsMismatch] = new(400, "Har bir partiya uchun sanalgan miqdorni kiriting", "Укажите подсчитанное количество для каждой партии", "Ҳар бир партия учун саналган миқдорни киритинг"),
        [ErrorCodes.AdjustmentNotPending] = new(409, "So'rov allaqachon ko'rib chiqilgan", "Запрос уже рассмотрен", "Сўров аллақачон кўриб чиқилган"),
        [ErrorCodes.ImportFailed] = new(400, "Faylda xatolar bor", "В файле есть ошибки", "Файлда хатолар бор"),
        [ErrorCodes.UnknownReport] = new(404, "Bunday hisobot yo'q", "Такого отчёта нет", "Бундай ҳисобот йўқ")
    };

    public static int StatusOf(string code) => Entries.TryGetValue(code, out var entry) ? entry.Status : 400;

    public static string MessageOf(string code) =>
        Entries.TryGetValue(code, out var entry) ? Languages.Pick(entry.Uz, entry.Ru, entry.Cyrl) : code;
}
