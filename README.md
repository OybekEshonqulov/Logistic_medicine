# PharmaTrack backend

Dorilarning markaziy ombordan dilerlarga, dilerlardan dorixonalarga borishini kuzatadigan va rahbarga tahlil beradigan tizimning backend qismi. Web va mobil ilovalar shu API bilan ishlaydi.

```
Yetkazib beruvchi → Markaz (kirim) → Diler (qabul, qoldiq) → Dorixona (diler kiritgan sotuv)
```

## Texnologiyalar

ASP.NET Core (.NET 10), EF Core, PostgreSQL, FluentValidation, Serilog, ClosedXML, Swagger.

## Loyiha tuzilishi

| Papka | Vazifasi |
|---|---|
| `src/Domain` | Entity'lar va biznes qoidalari, tashqi kutubxonalarga bog'liq emas |
| `src/Application` | Servislar, DTO va validatorlar, funksiya bo'yicha `Features/` ichida |
| `src/Infrastructure` | EF Core, migratsiyalar, JWT, Excel, qoldiq jurnali |
| `src/Api` | Controller'lar, xatolarni qayta ishlash, sozlamalar |
| `tests/Tests` | Unit testlar va haqiqiy PostgreSQL'dagi integration testlar |

## Ishga tushirish

Kerak bo'ladi: .NET 10 SDK va PostgreSQL 17 (yoki `docker compose up postgres`).

`.env.example` dagi qiymatlarni muhit o'zgaruvchilari yoki user-secrets orqali bering:

```bash
cd src/Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=pharmatrack;Username=postgres;Password=PAROL"
dotnet user-secrets set "Jwt:SigningKey" "KAMIDA-32-BELGILI-TASODIFIY-KALIT"
dotnet user-secrets set "Seed:AdminPhone" "+998900000000"
dotnet user-secrets set "Seed:AdminPassword" "KUCHLI-PAROL"
dotnet run
```

Birinchi ishga tushishda migratsiyalar qo'llanadi, markaziy ombor, 14 ta hudud, 209 ta tuman va shahar hamda birinchi administrator yaratiladi. Swagger: `/swagger`. Holat tekshiruvi: `/health`.

## Testlar

```bash
export TEST_DB_CONNECTION="Host=localhost;Port=5432;Username=postgres;Password=PAROL"
dotnet test
```

Testlar vaqtinchalik baza yaratib, oxirida o'chiradi.

## Yangi migratsiya

```bash
dotnet ef migrations add NomiBilan -p src/Infrastructure -s src/Api -o Persistence/Migrations
```

## Asosiy qoidalar

- **Qoldiq faqat jurnal orqali o'zgaradi.** Har bir o'zgarish `stock_movements` ga yoziladi va o'chirilmaydi. `GET /api/v1/analytics/reconciliation` balans va jurnal mosligini tekshiradi.
- **Qoldiq manfiy bo'lmaydi.** Bir vaqtdagi sotuvlar ham ortiqcha tovar sota olmaydi.
- **Sotuv tahrirlanmaydi.** Xato sotuv bekor qilinadi (`reverse`) va qayta kiritiladi.
- **FEFO:** partiya tanlanmasa, muddati eng yaqin partiyadan yechiladi.
- **Hisobdan chiqarish** faqat Admin tasdiqlagandan keyin qoldiqni o'zgartiradi.
- **Hududiy tahlil:** sotuv dorixona manziliga, qoldiq diler manziliga bog'lanadi.

## Rollar

| Rol | Vazifasi |
|---|---|
| `Admin` | Hammasi, jumladan foydalanuvchilar va tasdiqlashlar |
| `Warehouse` | Markaz ombori: kirim, jo'natma, ma'lumotnomalar |
| `Executive` | Rahbar: dashboard va hisobotlar, faqat ko'rish |
| `Dealer` | Faqat o'z qoldig'i, qabul, sotuv va inventarizatsiya; kirim narxini ko'rmaydi |

## Hujjatlar

- [Frontend va mobil uchun API qo'llanmasi](docs/API_GUIDE.md)
- [Rahbar dashboardi](docs/DASHBOARD.md)
- [Viloyat va tumanlar](docs/REGIONS.md)
