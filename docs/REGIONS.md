# Viloyat va tumanlar

14 ta hudud `regions` jadvalida, 209 ta tuman va shahar `districts` jadvalida turadi. Ular birinchi ishga tushishda `src/Infrastructure/Persistence/Seed/regions.json` faylidan yuklanadi. Keyingi ishga tushishlarda faqat yetishmaganlari qo'shiladi, administrator o'zgartirgan nomlar ustidan yozilmaydi.

| Endpoint | Vazifasi |
|---|---|
| `GET /api/v1/regions` | Hududlar |
| `GET /api/v1/districts?regionId=` | Hudud tumanlari va shaharlari |
| `POST`, `PUT`, `DELETE` | Admin va Omborchi uchun tahrirlash |

Har bir nom uch yozuvda saqlanadi: `nameUz`, `nameRu`, `nameCyrl`. Yangi yozuvda uchalasi ham majburiy. Oldindan bor yozuvlarda krillcha nom bo'sh bo'lsa, ishga tushishda ro'yxatdan to'ldiriladi. Qidiruv uchala yozuvda ishlaydi, buning uchun baza Unicode qoidalari bilan yaratilgan bo'lishi kerak (`postgres:17` obrazida shunday, `C` lokalda krill harflari katta-kichikligi farqlanadi).

Diler va dorixonada `regionId` majburiy, `districtId` ixtiyoriy. Tuman berilsa, u shu hududga tegishli bo'lishi kerak.

## Manba

Boshlang'ich ro'yxat MIT litsenziyali [nurbekjummayev/laravel-region](https://github.com/nurbekjummayev/laravel-region) paketidan olingan va tozalangan: ruscha nomlardagi lotin harflari, apostroflar va ayrim yozuv xatolari tuzatilgan. Krillcha nomlar lotinchadan qoida bo'yicha o'girilgan. Bu rasmiy davlat manbasi emas, shuning uchun ishlatishdan oldin bir marta ko'zdan kechirish tavsiya etiladi. `Bo'z tumani` uchun manbada kod yo'q, shuning uchun uning `code` maydoni bo'sh.
