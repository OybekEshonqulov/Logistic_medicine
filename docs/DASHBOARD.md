# Rahbar dashboardi

Bitta so'rov butun ekranni to'ldiradi, shuning uchun hamma raqam bir vaqtga tegishli va bir-biriga mos bo'ladi:

```
GET /api/v1/analytics/dashboard?from=2026-10-01&to=2026-10-31&regionId=&districtId=&dealerId=&productId=
```

Filtrlarning hammasi ixtiyoriy, standart davr oxirgi 30 kun. `regionId` tanlansa, javobda `districts[]` ham to'ladi. Faqat `Admin` va `Executive` uchun (`analytics.dashboard` ruxsati).

## Ekran va maydonlar

| Ekrandagi qism | Javobdagi maydon | Nimani ko'rsatadi |
|---|---|---|
| Asosiy kartalar | `kpis.salesAmount`, `salesUnits`, `grossMargin` | `value`, `previousValue`, `changePercent`: oldingi teng davrga nisbatan o'zgarish |
| Foyda ulushi | `kpis.marginPercent` | Sotuvdagi yalpi foyda foizi |
| Qoldiq kartalari | `kpis.stockUnits`, `stockValue`, `inTransitUnits`, `inTransitValue` | Hozirgi tovar va yo'ldagi tovar |
| Zaxira | `kpis.daysOfStock` | Qoldiq joriy sotuv tezligida necha kunga yetadi |
| Dilerlar intizomi | `kpis.activeDealers`, `reportingDealers` | Nechta diler o'z vaqtida sotuv kiritgan |
| Yo'qotish xavfi | `kpis.expiringValue`, `expiredValue`, `writeOffValue`, `openDiscrepancies` | Pul ko'rinishidagi xavf |
| Sotuv grafigi | `salesTrend[]`, `granularity` | `amount`, `units`, `previousAmount`; davrga qarab kun, hafta yoki oy |
| Hududlar xaritasi yoki reytingi | `regions[]` | Viloyat bo'yicha sotuv, ulush, o'sish, qoldiq, zaxira kuni, diler va dorixona soni |
| Tumanlar kesimi | `districts[]` | Hudud tanlanganda: har bir tuman va shahar bo'yicha sotuv, ulush, o'sish, qoldiq |
| Top dorilar | `topProducts[]` | Eng ko'p sotilgan 10 ta dori va holati |
| Dilerlar jadvali | `dealers[]` | Sotuv, o'sish, qoldiq, zaxira, oxirgi sotuv, `reporting` holati |
| Qoldiq taqsimoti | `distribution` | Markaz, dilerlar va yo'ldagi tovar |
| Muddat diagrammasi | `expiryBuckets[]` | `expired`, `0-30`, `31-60`, `61-90`, `90+` kun: dona va qiymat |
| Zaxira salomatligi | `stockHealth[]` | `Stockout`, `Critical`, `Healthy`, `Overstock`, `NoSales` bo'yicha dori soni |
| "Bugun nimaga e'tibor berish kerak" | `attention[]` | `severity` bo'yicha tartiblangan, ko'pi bilan 20 ta muammo, `entityId` orqali o'tish mumkin |

## Hisoblash qoidalari

- **Sotuv** sof hisoblanadi: bekor qilingan sotuvlar kirmaydi, tasdiqlangan dorixona qaytarishlari ayriladi.
- **Foyda** = sotuv summasi − partiyaning kirim narxi. Bu dilerning dorixonaga sotgan narxidan kelib chiqadi.
- **Zaxira kuni** = qoldiq ÷ (davrdagi sotuv ÷ davr kunlari).
- **Qiymat** kirim narxida hisoblanadi.
- **Hudud yoki tuman filtri** tanlansa, markaziy ombor qoldig'i kirmaydi, chunki u hech qaysi hududga tegishli emas.

## Chegaralar `appsettings.json` da (`Alerts`)

| Sozlama | Standart | Ma'nosi |
|---|---|---|
| `ExpiryWarningDays` | 90 | Muddati yaqin deb hisoblash |
| `LowStockDays` | 14 | Bundan kam kunga yetsa `Critical` |
| `OverstockDays` | 90 | Bundan ko'p kunga yetsa `Overstock` |
| `ShipmentStuckDays` | 3 | Shuncha kun qabul qilinmagan jo'natma |
| `SalesReportGraceDays` | 2 | Shuncha kun sotuv kiritmagan diler "kechikkan" |

## Batafsil hisobotlar

Dashboard'dan chuqurroq ko'rish uchun:

| Endpoint | Natija |
|---|---|
| `analytics/sales?groupBy=` | `Day`, `Week`, `Month`, `Region`, `District`, `Dealer`, `Pharmacy`, `Product` |
| `analytics/stock?groupBy=` | `Location`, `Region`, `Product`, `Batch` |
| `analytics/expiring?withinDays=` | Muddati yaqin partiyalar |
| `analytics/days-of-stock` | Har bir dorining zaxira kuni va holati |
| `analytics/dead-stock?days=60` | Shuncha kun sotilmagan tovar |
| `exports/{report}` | Excel fayl |
