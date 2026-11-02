# Frontend va mobil dasturchilar uchun qo'llanma

To'liq ro'yxat Swagger'da: `/swagger`, klient kodi uchun `/swagger/v1/swagger.json`.

## Umumiy kelishuvlar

| Mavzu | Kelishuv |
|---|---|
| Asosiy manzil | `/api/v1` |
| Auth | `Authorization: Bearer <accessToken>` |
| Til | `Accept-Language: uz` yoki `ru`, standart `uz` |
| Sana | `DateOnly`: `2026-11-02`; vaqt: ISO 8601 UTC |
| Enum | Matn sifatida: `"status": "InTransit"` |
| Identifikator | GUID |
| O'chirish | `DELETE` faolsizlantiradi (`isActive: false`) |

## Sahifalash

Ro'yxatlar `?page=1&pageSize=20&search=...` qabul qiladi (`pageSize` ko'pi bilan 200):

```json
{ "items": [], "page": 1, "pageSize": 20, "totalCount": 0, "totalPages": 0 }
```

## Xatolar

Hamma xato bir xil ko'rinishda (`application/problem+json`). Mantiq uchun `code` dan foydalaning, `title` foydalanuvchiga ko'rsatiladi:

```json
{
  "status": 409,
  "title": "Qoldiq yetarli emas",
  "code": "stock.insufficient",
  "traceId": "...",
  "errors": { "phone": ["..."] },
  "details": []
}
```

`errors` faqat validatsiya xatosida (`common.validation_failed`), `details` esa qo'shimcha ma'lumot bo'lganda keladi. Masalan, Excel importida `details` xato qatorlar ro'yxati bo'ladi. Barcha kodlar: `src/Domain/Common/ErrorCodes.cs`.

Validatsiya matnlari kutubxonadan keladi va `ru` tilida tarjima qilingan, o'zbekcha uchun esa inglizcha chiqadi. Foydalanuvchiga maydon xatosini klient tomonda ko'rsating yoki `common.validation_failed` matnidan foydalaning.

## Kirish

1. `POST /auth/login` `{ phone, password }` → `accessToken`, `refreshToken`, `expiresIn`, `user`.
2. `user.permissions` orqali ekranlarni ko'rsating yoki yashiring. Masalan, `analytics.dashboard`.
3. `401` kelsa, `POST /auth/refresh` bilan yangilang va so'rovni takrorlang.
4. Refresh token bir martalik: har safar yangisini saqlang. Eskisi qayta yuborilsa, foydalanuvchining barcha sessiyalari tugaydi, shuning uchun bir vaqtda ikkita refresh yubormang.

## Jo'natma jarayoni

```
Draft → Approved → InTransit → Received
  └───────┴─→ Cancelled
```

| Qadam | Endpoint | Kim |
|---|---|---|
| Yaratish | `POST /shipments` | Markaz (`toDealerId` bilan), diler (qaytarish uchun, `toDealerId` siz) |
| Tasdiqlash | `POST /shipments/{id}/approve` | Markaz |
| Yuborish | `POST /shipments/{id}/send` | Jo'natuvchi, qoldiq shu yerda kamayadi |
| Qabul | `POST /shipments/{id}/receive` | Qabul qiluvchi, har bir qator majburiy |
| Farqni hal qilish | `POST /shipments/{id}/items/{itemId}/resolve-discrepancy` | Admin |

## Mobil: internetsiz ishlash

1. `GET /sync/references` ma'lumotnomalar va qoldiqni beradi. Javobdagi `serverTime` ni saqlang va keyingi safar `?updatedSince=` bilan yuboring.
2. Sotuvni lokal saqlang va **har bir sotuv uchun bir marta** UUID yarating.
3. Internet paydo bo'lganda `POST /sales` ni `Idempotency-Key: <uuid>` sarlavhasi bilan yuboring.
4. Javob: `201` yangi yaratildi, `200` oldin qabul qilingan (bu ham muvaffaqiyat). Javob kelmasa, aynan **shu kalit** bilan qayta yuboring.
5. `409 stock.insufficient` kelsa, sotuv qabul qilinmagan: dilerga ko'rsatib, tuzattiring.

`saleDate` o'tgan sana bo'lishi mumkin, shuning uchun haqiqiy sotuv kunini yuboring.

## Endpointlar ro'yxati

| Modul | Endpointlar |
|---|---|
| Auth | `auth/login`, `refresh`, `logout`, `me`, `change-password` |
| Ma'lumotnomalar | `users`, `regions`, `dealers`, `pharmacies`, `products`, `suppliers`, `manufacturers` |
| Kirim | `receipts`, `receipts/import`, `receipts/import/template` |
| Qoldiq | `batches`, `stock/balances`, `stock/movements` |
| Jo'natma | `shipments` va amallari |
| Sotuv | `sales`, `sales/{id}/reverse` |
| Tuzatish | `stock-counts`, `adjustments` |
| Analitika | `analytics/dashboard`, `summary`, `sales`, `stock`, `expiring`, `days-of-stock`, `dead-stock`, `dealers`, `reconciliation` |
| Eksport | `exports/{sales,stock,expiring,dealers}` (Excel) |
| Ogohlantirish | `notifications`, `unread-count`, `{id}/read`, `read-all` |
| Mobil | `sync/references` |
