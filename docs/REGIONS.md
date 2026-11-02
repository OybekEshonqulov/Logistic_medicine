# Viloyat va tumanlar

14 ta hudud `regions` jadvalida, 209 ta tuman va shahar `districts` jadvalida turadi. Ular birinchi ishga tushishda `src/Infrastructure/Persistence/Seed/regions.json` faylidan yuklanadi. Keyingi ishga tushishlarda faqat yetishmaganlari qo'shiladi, administrator o'zgartirgan nomlar ustidan yozilmaydi.

| Endpoint | Vazifasi |
|---|---|
| `GET /api/v1/regions` | Hududlar |
| `GET /api/v1/districts?regionId=` | Hudud tumanlari va shaharlari |
| `POST`, `PUT`, `DELETE` | Admin va Omborchi uchun tahrirlash |

Diler va dorixonada `regionId` majburiy, `districtId` ixtiyoriy. Tuman berilsa, u shu hududga tegishli bo'lishi kerak.

## Manba

Boshlang'ich ro'yxat MIT litsenziyali [nurbekjummayev/laravel-region](https://github.com/nurbekjummayev/laravel-region) paketidan olingan va tozalangan: ruscha nomlardagi lotin harflari, apostroflar va ayrim yozuv xatolari tuzatilgan. Bu rasmiy davlat manbasi emas, shuning uchun ishlatishdan oldin bir marta ko'zdan kechirish tavsiya etiladi. `Bo'z tumani` uchun manbada kod yo'q, shuning uchun uning `code` maydoni bo'sh.
