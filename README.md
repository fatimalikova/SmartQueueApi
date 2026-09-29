# Smart Queue API

## Layihə haqqında
Kiçik xidmət mərkəzi üçün sadə növbə idarəetmə API-si. Müştərilər növbəyə
əlavə olunur, növbəti müştəri çağırılır, növbədəki mövqe göstərilir və
müştəri növbədən silinə bilir.

## Texnologiyalar
- Node.js (18+)
- Express
- SQLite (better-sqlite3)

## Database
SQLite, fayl əsaslıdır (`queue.db`), ilk işə salınmada cədvəl avtomatik yaranır.

Cədvəl `customers`:

| Sütun      | Tip     | Qeyd                                   |
|------------|---------|----------------------------------------|
| id         | INTEGER | PK, autoincrement                      |
| name       | TEXT    | məcburi                                |
| status     | TEXT    | `Waiting` / `Serving` / `Completed`    |
| created_at | TEXT    | UTC ISO-8601                           |

## İşə salınması
```bash
git clone <repo-url>
cd smart-queue-api
npm install
npm start
```
Server `http://localhost:3000` ünvanında açılır (`PORT`, `DB_PATH` env dəyişənləri ilə dəyişmək olar).

## API endpoint-lər

| Metod  | URL                        | Təsvir                                  |
|--------|----------------------------|-----------------------------------------|
| POST   | /api/queue                 | Yeni müştəri əlavə et (`{"name":"Ali"}`) |
| GET    | /api/queue                 | Bütün gözləyən müştərilər (mövqe ilə)   |
| GET    | /api/queue/{id}            | Müştərinin məlumatı və növbədəki mövqeyi |
| POST   | /api/queue/next            | Növbəti müştərini çağır (Waiting → Serving) |
| DELETE | /api/queue/{id}            | Müştərini növbədən sil                  |
| POST   | /api/queue/{id}/complete   | (Bonus) Serving → Completed             |

### Nümunələr
```bash
curl -X POST localhost:3000/api/queue -H "Content-Type: application/json" -d '{"name":"Ali"}'
curl localhost:3000/api/queue
curl localhost:3000/api/queue/1
curl -X POST localhost:3000/api/queue/next
curl -X DELETE localhost:3000/api/queue/1
```

Nümunə cavab:
```json
{ "id": 1, "name": "Ali", "status": "Waiting",
  "createdAt": "2026-09-29T10:00:00.000Z", "position": 1 }
```
`position` yalnız `Waiting` statusunda olan müştərilər üçün qaytarılır, digər hallarda `null`.

Status kodları: 201 (yaradıldı), 204 (silindi), 400 (yanlış input), 404 (tapılmadı / növbə boşdur).

## `/next` üçün həll (Concurrency)
Problem: iki sorğu eyni anda gəlsə, hər ikisi əvvəl "ilk Waiting müştəri kimdir?"
sorğusunu edib eyni müştərini seçə bilər (check-then-act race condition).

Həll: seçim və status dəyişikliyi **tək atomik SQL əmrində** edilir:

```sql
UPDATE customers SET status = 'Serving'
WHERE status = 'Waiting'
  AND id = (SELECT id FROM customers WHERE status = 'Waiting' ORDER BY id LIMIT 1)
RETURNING *;
```

- SQLite eyni anda yalnız bir yazma əməliyyatına icazə verir, ona görə iki
  sorğu ardıcıl icra olunur.
- İkinci sorğu artıq `Serving` olmuş müştərini görmür və növbəti `Waiting`
  müştərini götürür (və ya heç kim yoxdursa 404 qaytarır).
- Əlavə olaraq `WHERE status = 'Waiting'` şərti ikinci müdafiə qatıdır.

PostgreSQL/MySQL-də eyni məqsəd üçün `SELECT ... FOR UPDATE SKIP LOCKED`
transaksiya daxilində istifadə olunardı. Bir neçə server instansı olduqda
da bu yanaşma işləyir, çünki kilid tətbiqin yaddaşında yox, database
səviyyəsindədir.
