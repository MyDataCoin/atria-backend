-- Разовое приведение валюты в базе к кыргызскому сому (KGS).
--
-- ЗАЧЕМ. Валюта была свободным полем: домен требовал лишь непустую строку, а валидатор — ровно три
-- буквы, поэтому «TJS» (сомони, валюта Таджикистана) проходил проверку так же легко, как «KGS».
-- Ничего вниз по течению не конвертирует: неверный код просто переименовывает все суммы объекта.
-- Код это больше не пропустит (Atria.Domain.Common.Money), но уже записанные строки должен
-- исправить этот скрипт.
--
-- ЧТО ДЕЛАЕТ. Переписывает валюту на KGS в объектах, заявках, выплатах, обязательствах возврата и
-- налоговых справках. Суммы НЕ пересчитываются: цифры вводились как сомы, ошибочной была подпись.
-- Если где-то суммы действительно вводились в другой валюте — не запускать, сначала разобраться.
--
-- НЕ ТРОГАЕТ travel_rule_messages: уведомление о переводе может законно называть валюту
-- контрагентского VASP, платформа её сообщает, а не назначает.
--
-- ПРОВЕРИТЬ ПЕРЕД ЗАПУСКОМ. Первый SELECT ничего не меняет: он показывает, что будет затронуто.
--
--   psql "$CONNECTION_STRING" -f scripts/normalize-currency-to-som.sql
--
-- Транзакция: либо всё, либо ничего.

BEGIN;

-- 0. Что найдено (только чтение).
SELECT 'properties' AS entity, "Currency", count(*) AS rows
FROM properties WHERE upper("Currency") <> 'KGS' GROUP BY "Currency"
UNION ALL
SELECT 'investments', "Currency", count(*)
FROM investments WHERE upper("Currency") <> 'KGS' GROUP BY "Currency"
UNION ALL
SELECT 'payout_runs', "Currency", count(*)
FROM payout_runs WHERE upper("Currency") <> 'KGS' GROUP BY "Currency"
UNION ALL
SELECT 'refund_obligations', "Currency", count(*)
FROM refund_obligations WHERE upper("Currency") <> 'KGS' GROUP BY "Currency"
UNION ALL
SELECT 'tax_statements', "Currency", count(*)
FROM tax_statements WHERE upper("Currency") <> 'KGS' GROUP BY "Currency";

-- 1. Объекты — источник валюты для всего остального.
UPDATE properties SET "Currency" = 'KGS' WHERE "Currency" <> 'KGS';

-- 2. Заявки: валюта снимается с объекта в момент покупки.
UPDATE investments SET "Currency" = 'KGS' WHERE "Currency" <> 'KGS';

-- 3. Выплаты: валюта приходила прямо из тела запроса, ни с чем не сверяясь.
UPDATE payout_runs SET "Currency" = 'KGS' WHERE "Currency" <> 'KGS';

-- 4. Обязательства возврата и налоговые справки — производные от заявок.
UPDATE refund_obligations SET "Currency" = 'KGS' WHERE "Currency" <> 'KGS';
UPDATE tax_statements SET "Currency" = 'KGS' WHERE "Currency" <> 'KGS';

-- 5. Контроль: после коммита ни одна строка не должна вернуться.
SELECT 'properties' AS entity, count(*) AS remaining FROM properties WHERE "Currency" <> 'KGS'
UNION ALL SELECT 'investments', count(*) FROM investments WHERE "Currency" <> 'KGS'
UNION ALL SELECT 'payout_runs', count(*) FROM payout_runs WHERE "Currency" <> 'KGS'
UNION ALL SELECT 'refund_obligations', count(*) FROM refund_obligations WHERE "Currency" <> 'KGS'
UNION ALL SELECT 'tax_statements', count(*) FROM tax_statements WHERE "Currency" <> 'KGS';

COMMIT;
