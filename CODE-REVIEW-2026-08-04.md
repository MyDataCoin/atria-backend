# Code review ATRIA — 4 августа 2026

> **СТАТУС: все находки исправлены (4 августа 2026).** Каждый пункт ниже помечен ✅ с указанием, что
> именно сделано. Проверка после правок:
>
> | Набор | Было | Стало |
> |---|---|---|
> | `forge test` (контракты) | 46/46 | **61/61** (+15 новых, включая тесты на C-1) |
> | `dotnet test Atria.sln` | 472/472 | **482/482** (+10 новых регрессий) |
> | `npm audit --omit=dev` (админка) | 1 high, 1 low | **0** |
> | `npm audit --omit=dev` (инвестор) | 1 high, 1 moderate, 1 low | **0** |
> | `npm run lint` | заглушка `echo` | **настоящий ESLint**, 0 ошибок |
>
> Две правки — намеренно частичные, см. «Что осталось» в конце документа.


Охвачены 4 репозитория:

| Репозиторий | Путь | Что смотрел |
|---|---|---|
| Смарт-контракты | `/Users/suzan/Desktop/atria-contracts` | **глубокий аудит** (Solidity, deploy-скрипты, инварианты, CI) |
| Бэкенд | `/Users/suzan/Desktop/atria-backend` | auth/JWT/OTP, BOLA, webhooks, крипто, on-chain интеграция, конфиги, Docker, CI |
| Админ-дашборд | `/Users/suzan/Desktop/atria-admin-dashboard` | хранение токенов, XSS-стоки, зависимости, CI |
| Дашборд инвестора | `/Users/suzan/Desktop/atria-investor-dashboard` | то же |

**Применённые security-скиллы:** `testing-jwt-token-security`, `testing-api-for-broken-object-level-authorization`, `testing-api-authentication-weaknesses`, `implementing-api-rate-limiting-and-throttling`, `implementing-secret-scanning-with-gitleaks` (ручной эквивалент — gitleaks не установлен), `performing-sca-dependency-scanning-with-snyk` (эквивалент: `npm audit`, `dotnet list package --vulnerable`), `hardening-docker-containers-for-production`, `testing-for-xss-vulnerabilities`, `testing-api-for-mass-assignment-vulnerability`, `implementing-gdpr-data-protection-controls`.

**Состояние тестов на момент ревью:**
- `forge test` (контракты) — **46/46 passed**, включая 6 stateful-инвариантов.
- `dotnet test Atria.sln` — **472/472 passed** (233 Domain + 169 Application + 70 Integration).
- `dotnet list package --vulnerable --include-transitive` — уязвимых пакетов нет.

Общая оценка: код **сильно выше среднего** по дисциплине — object-level authorization проведена последовательно, refresh-ротация с reuse-detection есть, вебхуки проверяются HMAC + timestamp в constant-time, AES-GCM реализован корректно, деплой-скрипт контрактов складывает права и снимает их с деплойера. Найденное ниже — это в основном **конфигурация, поверхность аутентификации и один настоящий дефект контракта**.

---

## Сводка

| # | Severity | Репо | Проблема |
|---|---|---|---|
| C-1 | ✅ 🔴 Критическая | contracts | `registerDid` позволяет захватить чужой DID навсегда |
| C-2 | ✅ 🔴 Критическая | backend | Плейсхолдеры секретов в закоммиченном `appsettings.json`, нет fail-closed проверки |
| C-3 | ✅ 🔴 Критическая | backend | `/auth/admin/login` и `/auth/realtor/login` не покрыты rate limiter'ом |
| M-1 | ✅ 🟠 Средняя | backend | `Otp__MagicCode: "111111"` в прод-compose |
| M-2 | ✅ 🟠 Средняя | обе панели | Refresh-токен в `localStorage` |
| M-3 | ✅ 🟠 Средняя | admin | Гонка в `doRefresh` → массовый разлогин |
| M-4 | ✅ 🟠 Средняя | backend | `ExternalBlockchainSigner` ходит в custody без аутентификации |
| M-5 | ✅ 🟠 Средняя | backend | BCrypt cost 12 на анонимном OTP-эндпоинте = усилитель DoS |
| M-6 | ✅ 🟠 Средняя | backend | IP при `request-otp` принимается и выбрасывается |
| M-7 | ✅ 🟠 Средняя | contracts | `slither.config.json` есть, Slither в CI нет |
| M-8 | ✅ 🟠 Средняя | contracts | Одношаговая передача owner/authority |
| M-9 | ✅ 🟠 Средняя | contracts | `Deploy.s.sol` не проверяет разделение ролей до броадкаста |
| M-10 | ✅ 🟠 Средняя | все | Нет SCA / secret scanning в CI; `npm run lint` — заглушка |
| M-11 | ✅ 🟠 Средняя | backend+contracts | Селекторы контракта захардкожены в C# без теста на соответствие ABI |
| M-12 | ✅ 🟠 Средняя | backend | Access-токен нельзя отозвать (бан действует через 15 мин) |
| M-13 | ✅ 🟠 Средняя | обе панели | `postcss` (high), `body-parser`, `protobufjs` |
| M-14 | ✅ 🟠 Средняя | все | Деплой по SSH с паролем |
| M-15 | ✅ 🟠 Средняя | backend | `POST /appeals` анонимный и без лимита |
| S-1…S-18 | ✅ 🟡 Маленькая | — | см. раздел ниже |

---

## 🔴 Критические

### C-1. `IdentityRegistry.registerDid` — постоянный захват чужого DID
**Файл:** `atria-contracts/src/IdentityRegistry.sol:106-130`
**Контракт задеплоен:** BNB testnet, `0x3838f73f9787f8b4f8a1e0173de7c7030a570806` (см. `appsettings.json → Blockchain:Networks`).

Комментарий в коде утверждает: *«requiring a controller signature ensures only the DID controller can create the anchor»*. Это неверно. Подпись доказывает только, что ключ `controller` подписал `(didHash, attestationRoot, chainid, address(this))` — но **ничто не связывает `didHash` с этим ключом**. Атакующий генерирует свою пару ключей, подписывает ею *чужой* `didHash` и вызывает `registerDid`.

Последствия:
1. `a.exists` становится `true`, повторная регистрация невозможна (`AlreadyRegistered`), функции удаления/переназначения анкера **нет вообще**. Легитимный контроллер (в нашем случае — бэкенд с `Anchor:AgentPrivateKey`) заблокирован навсегда: его `updateRoot` упадёт на `NotOwner`.
2. В реестре навсегда остаётся `attestationRoot`, выбранный атакующим. Любой off-chain верификатор, который спрашивает «был ли у держателя root R» и доверяет `getAnchor(didHash).attestationRoot`, получает подделку.
3. `didHash = SHA-256(utf8(did))`, а DID выводится из идентификатора пользователя → перебираем/предсказываем и захватываем пачкой. Плюс тривиальный front-run транзакции бэкенда в мемпуле.

**Как чинить (выбрать одно, лучше 1+3):**
1. Закрыть регистрацию ролью: добавить `onlyAuthority` (или отдельную `REGISTRAR_ROLE`) на `registerDid`. Бэкенд и так единственный, кто её вызывает — подпись контроллера остаётся как второй фактор, но захват снаружи исчезает.
2. Либо привязать хэш к ключу: требовать `didHash == sha256("did:pkh:eip155:<chainid>:<controller>")` — тогда чужой DID подписать нечем.
3. В любом случае добавить путь восстановления: `reassignAnchor(bytes32 didHash, address newOwner)` под `onlyAuthority` с событием, чтобы захваченный/потерянный анкер не был бетонной стеной.
4. Добавить тест: `test_RevertWhen_StrangerRegistersSomeoneElsesDid` и invariant «анкер всегда принадлежит authority-одобренному контроллеру».

---

### C-2. Плейсхолдеры секретов в закоммиченном `appsettings.json` без fail-closed защиты
**Файл:** `src/Atria.Api/appsettings.json:26-31, 40, 60-64`

В репозитории лежат рабочие значения-по-умолчанию:

| Ключ | Значение в репо | Что даёт при попадании в прод |
|---|---|---|
| `Jwt:SigningKey` | `dev-only-signing-key-not-a-real-secret-change-me-32+bytes` | подделка любого JWT, включая `role: SuperAdmin` (hashcat `-m 16500` не нужен — ключ прямо в git) |
| `Encryption:Key` | `MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=` = `0123456789abcdef0123456789abcdef` | расшифровка всех PII/KYC-полей в БД |
| `Didit:WebhookSecret` | `CHANGE_ME` | подделка вебхука → **самостоятельный аппрув собственного KYC** |
| `NikitaPro:ApiKey` / `Login` | `CHANGE_ME` | — |
| `ConnectionStrings:Postgres` | `Password=CHANGE_ME` | — |

Смягчение: `docker-compose.yml` использует `${JWT_SIGNING_KEY:?...}` / `${ENCRYPTION_KEY:?...}` / `${DIDIT_WEBHOOK_SECRET:?...}` — деплой через compose упадёт без реальных значений. Но:
- защита живёт **в файле оркестрации, а не в приложении**. Любой запуск мимо compose (`dotnet run`, k8s-манифест, systemd, staging) молча поднимется с ключом из git.
- `DiditKycProvider.VerifySignature` (`src/Atria.Infrastructure/Kyc/Providers/DiditKycProvider.cs:218`) проверяет только `IsNullOrEmpty`. Строка `CHANGE_ME` непустая → HMAC считается по известному всем секрету и проверка **проходит**.

**Как чинить:**
1. Убрать значения из `appsettings.json` — оставить пустые строки, а образец перенести в `appsettings.Example.json`.
2. Добавить стартовый guard (там же, где `BindValidated`, `src/Atria.Infrastructure/DependencyInjection.cs:86`): в Production бросать, если `Jwt:SigningKey`/`Encryption:Key`/`Didit:WebhookSecret`/`NikitaPro:ApiKey` пусты, равны `CHANGE_ME` или совпадают с известными dev-значениями (сравнить по SHA-256 со списком запрещённых).
3. Усилить `JwtOptions.SigningKey`: `MinLength(32)` пропускает парольную фразу. Требовать base64 от 32 случайных байт и валидировать это в `[CustomValidation]`.
4. `DiditOptions.WebhookSecret` — `[Required][MinLength(32)]` + явный отказ на `CHANGE_ME`.
5. Провернуть все секреты, которые могли уехать в прод с этими значениями.
6. Поставить gitleaks в pre-commit и в CI (см. M-10).

---

### C-3. Логин админа и риелтора не покрыт rate limiter'ом
**Файл:** `src/Atria.Api/Program.cs:198-228`

```csharp
string[] throttledPaths =
{
    "/api/v1/auth/login",                       // ← такого эндпоинта не существует
    "/api/v1/auth/register",
    "/api/v1/auth/register/phone/request-otp"
};
```

Реальные маршруты — `AuthController` (`[Route("api/v{version:apiVersion}/auth")]`):
- `POST /api/v1/auth/admin/login` (Admin **и SuperAdmin**) — `AuthController.cs:38`
- `POST /api/v1/auth/realtor/login` — `AuthController.cs:60`
- `POST /api/v1/auth/refresh` — `AuthController.cs:82`

Ни один не начинается с `/api/v1/auth/login`, поэтому лимитер возвращает им `GetNoLimiter`. То есть **самые привилегированные учётки платформы принимают неограниченное число попыток пароля**. Лимит `5/мин` работает только на OTP-ветке (там `/auth/register` корректно покрывает и `verify-otp` префиксом). Блокировки аккаунта после N неудач тоже нет — `AuthTokensFactory.IssueForCredentialLoginAsync` просто отдаёт 401.

Единственное, что стоит между атакующим и подбором — BCrypt cost 12 (~250 мс/попытка), то есть ~4 попытки/сек с одного соединения и линейное масштабирование по параллельным.

**Как чинить:**
1. Заменить префиксный список на явный, покрывающий фактические пути:
   ```csharp
   string[] throttledPaths =
   {
       "/api/v1/auth/admin/login",
       "/api/v1/auth/realtor/login",
       "/api/v1/auth/refresh",
       "/api/v1/auth/register",
   };
   ```
   Ещё надёжнее — навесить `[EnableRateLimiting("auth")]` на сам `AuthController`, тогда путь нельзя рассинхронизировать с маршрутом.
2. Добавить второе измерение партиции — **по username**, а не только по IP, иначе распределённый credential stuffing проходит мимо. Для паролей: sliding window 10/15 мин на username.
3. Добавить прогрессивную блокировку учётки (`users.failed_login_count` + `locked_until`) и запись в `AuditLog`.
4. Отдавать заголовок `Retry-After` в 429 (сейчас только код).
5. Добавить интеграционный тест: 6-я попытка `admin/login` за минуту → 429. Сейчас регресс такого рода тесты не ловят.

---

## 🟠 Средние («более менее»)

### M-1. `Otp__MagicCode: "111111"` в базовом (продовом) compose
**Файл:** `docker-compose.yml:90-92`, комментарий сам называет это «auth bypass».

Проверил: в коде **привязки нет** — `OtpOptions` (`src/Atria.Infrastructure/Configuration/OtpOptions.cs`) явно не содержит такого свойства и комментирует, почему магический код был удалён. Значит **сейчас переменная инертна**, полного захвата аккаунта по коду `111111` нет.

Но это мина: `docker-compose.yml` — файл, которым деплоится прод, и любой, кто вернёт свойство в `OtpOptions` «чтобы заработало как раньше», мгновенно откроет анонимный захват любого номера.

**Как чинить:** удалить строку и комментарий из `docker-compose.yml`. Если нужен способ логиниться при аварии SMS — сделать его через админский эндпоинт с аудитом, а не через глобальный код.

### M-2. Refresh-токен лежит в `localStorage`
**Файлы:** `atria-admin-dashboard/src/api/client.js:18-40`, `atria-investor-dashboard/src/api/client.js:23`

Refresh-токен живёт 30 дней (`JwtOptions.RefreshTokenDays = 30`) и хранится в `localStorage`, то есть доступен любому JS в origin. Один XSS (или скомпрометированный npm-пакет в бандле — их там 3 с известными CVE, см. M-13) = месячный захват сессии **SuperAdmin**. Ротация и reuse-detection на бэкенде тут не помогают: у атакующего валидный токен, ротацию он проведёт сам.

**Как чинить:**
1. Refresh-токен — в `HttpOnly; Secure; SameSite=Strict` cookie, выставляемую бэкендом на `/auth/*`. `POST /auth/refresh` читает cookie, а не тело.
2. Access-токен — в памяти JS (обычная переменная модуля), не в `localStorage`. Потеря при перезагрузке вкладки лечится тихим refresh'ем по cookie.
3. Сократить `RefreshTokenDays` для админской панели до 1–7 дней; 30 дней уместны инвестору, но не SuperAdmin.
4. Навесить CSP на сами панели (nginx-заголовок): сейчас CSP есть только на API-ответах (`SecurityHeadersMiddleware`), а сами SPA отдаёт nginx без неё.

### M-3. Гонка в `doRefresh` → массовый ложный разлогин
**Файл:** `atria-admin-dashboard/src/api/client.js:145-152`

```js
try {
  refreshInFlight = refreshInFlight || doRefresh();
  await refreshInFlight;
} finally {
  refreshInFlight = null;      // ← обнуляет ЛЮБОЙ участник, а не только инициатор
}
```

Дашборд грузит несколько запросов параллельно. Как только первый refresh завершился и `finally` сбросил флаг, второй запрос, чей 401 пришёл чуть позже, вызывает `doRefresh()` **со старым токеном из `tokenStore.refresh`**. Бэкенд (`RefreshTokenCommandHandler.cs:46-51`) видит уже отозванный токен, трактует это как утечку и вызывает `RevokeAllForUserAsync` — пользователя выкидывает из всех сессий, а в логах появляется ложный сигнал компрометации.

**Как чинить:**
```js
async function doRefresh() { /* ... */ }

function refreshOnce() {
  if (!refreshInFlight) {
    refreshInFlight = doRefresh().finally(() => { refreshInFlight = null; });
  }
  return refreshInFlight;
}
```
Сбрасывать флаг только внутри промиса-инициатора, и перечитывать `tokenStore.refresh` внутри `doRefresh` в момент вызова, а не до `await`.

### M-4. Запрос на подпись уходит в custody без аутентификации
**Файл:** `src/Atria.Infrastructure/Compliance/ExternalBlockchainSigner.cs:37-59`

`PostAsJsonAsync(endpoint, body, ct)` не несёт ни API-ключа, ни mTLS-сертификата, ни HMAC-подписи запроса. Всё, что защищает эмиссию долей, — сетевая изоляция `https://signer.atria.local`. Кто угодно внутри периметра (скомпрометированный под, SSRF в соседнем сервисе) может отправить `mint` на произвольный адрес.

**Как чинить:**
1. mTLS между API и подписантом (клиентский сертификат в `HttpClientHandler`), либо как минимум `Authorization`-заголовок со служебным токеном из секрета.
2. Подписывать сам запрос: HMAC-SHA256 над `(operationType|unsignedPayload|chainId|contract|timestamp|nonce)`, чтобы подписант отвергал всё, что не пришло от бэкенда, и не принимал повторы.
3. На стороне custody — политика/порог по `OperationType` (комментарий в коде это уже предполагает; проверить, что реализовано).
4. Задать `client.Timeout` и Polly-политику на этом `HttpClient` (сейчас дефолтные 100 с, ретраев нет).

### M-5. BCrypt cost 12 на анонимном OTP-эндпоинте — усилитель DoS
**Файлы:** `src/Atria.Infrastructure/Identity/OtpService.cs:56, 114`, `BcryptPasswordHasher.cs:9`

Каждый анонимный `request-otp` считает `BCrypt.HashPassword(code, 12)`, каждый `verify-otp` — `BCrypt.Verify`. Это ~250 мс CPU на запрос без аутентификации. Лимит 5/мин на IP не спасает от распределённой нагрузки: сотня IP × 5 = 500 хэшей/мин на инстанс. BCrypt здесь ещё и не нужен — код шестизначный, его стойкость даёт не медленный KDF, а лимит попыток.

**Как чинить:** хэшировать OTP через `HMACSHA256(pepper, phone + code)`, где `pepper` — отдельный секрет из конфига (не `Encryption:Key`). Сравнивать через `CryptographicOperations.FixedTimeEquals`. Пароли админов оставить на BCrypt cost 12 — там он к месту.

### M-6. IP при `request-otp` принимается и молча выбрасывается
**Файлы:** `AuthController.cs:113-118`, `RequestPhoneOtpCommandHandler.cs:14-16`, `OtpService.cs:44-51`

Контроллер честно достаёт `RemoteIpAddress`, команда несёт его через слой, а `OtpService.RequestAsync(string phone, string? ipAddress, ...)` **параметр `ipAddress` не использует ни разу**. Лимит считается только `CountRequestsSinceAsync(phone, ...)`. При этом XML-документация эндпоинта обещает: *«The caller's IP is captured here for rate limiting and abuse capture»* — то есть документация описывает несуществующий контроль.

Практически: атакующий с одного IP перебирает номера (по 5 кодов на каждый) и жжёт SMS-бюджет; никакого per-IP счётчика на уровне сервиса нет, только ASP.NET-лимитер 5/мин, который считает **путь+IP**, а не число разных номеров.

**Как чинить:**
1. Добавить в `IOtpCodeStore` подсчёт по IP и второй порог: `RequestsPerHourPerIp` (например 20) — а также порог на число **различных номеров** с одного IP за час.
2. Писать `ipAddress` в строку кода (колонка `requested_from_ip`) — это и есть обещанный «abuse capture», сейчас его нет.
3. Либо, если контроль не нужен, убрать параметр из сигнатуры и поправить документацию — молчаливо игнорируемый параметр хуже отсутствующего.

### M-7. Slither настроен, но в CI не запускается
**Файлы:** `atria-contracts/slither.config.json`, `.github/workflows/ci.yml`

`slither.config.json` аккуратно заполнен (`filter_paths: lib/`, remaps, informational/low включены), но в workflow есть только `forge fmt --check`, `forge build`, `forge test`, `forge coverage --report summary`. Статического анализа нет, и порога покрытия тоже нет — `coverage` печатает таблицу, но никогда не падает.

**Как чинить:** добавить джобу
```yaml
  slither:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { submodules: recursive }
      - uses: crytic/slither-action@v0.4.0
        with:
          fail-on: medium
          slither-config: slither.config.json
```
и завести порог покрытия (`forge coverage --report lcov` + проверка процента), иначе шаг остаётся декоративным.

### M-8. Одношаговая передача прав в `Allowlist` и `IdentityRegistry`
**Файлы:** `atria-contracts/src/Allowlist.sol:67-80`, `src/IdentityRegistry.sol:254-258`

`transferOwnership` и `transferAuthority` присваивают адрес сразу. Опечатка в адресе или адрес контракта без нужного интерфейса → права на аллоулист (то есть на весь трансфер-контроль токена) или на реестр эмитентов теряются навсегда. У токена та же тема: `AccessControl` без `AccessControlDefaultAdminRules`, `Deploy.s.sol:110-111` делает `grantRole(DEFAULT_ADMIN_ROLE, admin)` + `renounceRole(..., deployer)` в одной транзакции — если `cfg.admin` неверен, контракт остаётся без администратора.

**Как чинить:**
1. `Allowlist` и `IdentityRegistry`: двухшаговая передача (`pendingOwner` / `acceptOwnership`), проще всего — унаследовать OZ `Ownable2Step`.
2. Токен: заменить `AccessControl` на `AccessControlDefaultAdminRules` (двухшаговая передача админа + задержка).
3. В `CheckDeployment.s.sol` добавить проверку, что `admin` — контракт (`extcodesize > 0`), раз по README это должен быть мультисиг.

### M-9. `Deploy.s.sol` не проверяет разделение ролей до броадкаста
**Файл:** `atria-contracts/script/Deploy.s.sol:68-112`

Весь смысл ролевой схемы — «ни один ключ не может одновременно создавать доли и отбирать их» (комментарий в `AtriaPropertyToken.sol:26-32`). Но скрипт принимает `MINTER_ADDRESS == COMPLIANCE_ADDRESS` (или `== ADMIN_ADDRESS`, или нулевые/совпадающие адреса) и молча деплоит. Ловится это только постфактум, `CheckDeployment.s.sol:41-44`, когда контракт уже в сети и деньги за газ потрачены.

Отдельно: ветка «переиспользовать существующий аллоулист» (`_allowlist`, строка 88) возвращает адрес **не выдав прав `allowlistAgent`** — бэкенд-шлюз молча не сможет вести список.

**Как чинить:** добавить в начало `_config()` require-блок:
```solidity
require(cfg.minter != cfg.compliance, "minter == compliance");
require(cfg.admin != cfg.minter && cfg.admin != cfg.compliance, "admin holds an operational key");
require(cfg.oracle != cfg.minter && cfg.oracle != cfg.compliance, "oracle overlaps");
require(cfg.pauser != address(0) && cfg.oracle != address(0), "role unset");
```
и в ветке переиспользования аллоулиста явно логировать, что `setAgent` пропущен (или вызывать его, если деплойер — владелец).

### M-10. В CI нигде нет SCA и secret scanning; `npm run lint` — заглушка
**Файлы:** `atria-admin-dashboard/package.json:11`, `atria-investor-dashboard/package.json:11`, все три `.github/workflows/*.yml`

```json
"lint": "echo 'Linter bypassed for JS output'"
```
Оба CI-workflow'а честно вызывают `npm run lint` — и шаг всегда зелёный. Это хуже отсутствия шага: в отчёте о сборке стоит «Lint ✅».

Ни в одном из четырёх репозиториев нет: gitleaks/trufflehog, `npm audit --audit-level=high`, `dotnet list package --vulnerable`, Trivy на образ, CodeQL.

**Как чинить:**
1. Поставить реальный ESLint (`eslint`, `eslint-plugin-react-hooks`, `eslint-plugin-react`) или убрать шаг вообще, чтобы не создавать ложную уверенность.
2. Во все 4 репо — job `security`:
   ```yaml
   - uses: gitleaks/gitleaks-action@v2
     env: { GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }} }
   ```
3. Фронты: `npm audit --audit-level=high` (падает на текущем postcss, см. M-13). Бэкенд: `dotnet list package --vulnerable --include-transitive` + grep на `has the following vulnerable`.
4. Бэкенд: `trivy image atria-api:latest --severity HIGH,CRITICAL --exit-code 1` после сборки образа.
5. Gitleaks в pre-commit hook (`.pre-commit-config.yaml`) — это и есть страховка от повторения C-2.

### M-11. Селекторы контракта захардкожены в C# и ничем не проверяются
**Файл:** `src/Atria.Infrastructure/Compliance/CustodyTokenGateway.cs:45, 90`

```csharp
sha3Signature: "40c10f19",   // mint(address,uint256)
sha3Signature: "d2b3d0db",   // reportCollateral(bytes32,uint256,uint64,string)
```

Сверил с артефактами Foundry (`out/AtriaPropertyToken.sol/AtriaPropertyToken.json → methodIdentifiers`) — **оба верны сегодня**. Проблема в том, что это два репозитория без общего контракта: любое изменение сигнатуры в `AtriaPropertyToken.sol` (скажем, добавление `bytes32 reason` в `mint`) не сломает сборку бэкенда. Custody подпишет калldata с чужим селектором, транзакция ревертнётся на цепи — уже после списания газа, и в случае `CustodyTokenGateway` **без ответа о результате** (`Confirmed: false`, проверка отложена на finality-воркер).

**Как чинить:**
1. Экспортировать ABI из `forge build` в артефакт и коммитить `abi/AtriaPropertyToken.json` в бэкенд (или подтягивать в CI).
2. Добавить тест `AtriaPropertyTokenAbiTests`, который считает keccak-селектор из ABI и сравнивает с константами в `CustodyTokenGateway`. Пять строк, ловит весь класс дрейфа.
3. Ещё лучше — генерировать вызовы из ABI (Nethereum `FunctionMessage`, как уже сделано в `EvmTokenGateway.cs:164-190`), а не строкой.

### M-12. Access-токен нельзя отозвать
**Файлы:** `src/Atria.Infrastructure/Identity/JwtTokenGenerator.cs:44`, `src/Atria.Api/Program.cs:120-133`

`jti` в токен кладётся, но нигде не проверяется. Бан пользователя (`VerifyPhoneOtpCommandHandler.cs:52-56`), смена роли или выход из системы отзывают **только refresh**-токен. Действующий access-токен продолжает работать до 15 минут. Для роли `Compliance`/`SuperAdmin`, которая может жечь доли и делать forced transfer, 15 минут после увольнения/компрометации — существенно.

**Как чинить:**
1. Denylist по `jti` в Redis/таблице с TTL = `AccessTokenMinutes`; проверять в `JwtBearerEvents.OnTokenValidated`. Дёшево: записей мало, живут 15 минут.
2. Либо `security_stamp` на пользователе: класть в токен, при бане/смене роли инкрементировать, сверять в `OnTokenValidated`.
3. Для привилегированных ролей снизить `AccessTokenMinutes` до 5.

### M-13. Уязвимые npm-пакеты в обеих панелях
`npm audit --omit=dev` (запущен 4 августа):

| Пакет | Severity | Где | CVE |
|---|---|---|---|
| `postcss` ≤8.5.22 | **high** | обе | GHSA-r28c-9q8g-f849, GHSA-fxqj-rqcc-2cmp (path traversal через `sourceMappingURL`) |
| `body-parser` <1.20.6 | low | обе | GHSA-v422-hmwv-36x6 (DoS) |
| `protobufjs` 7.5.0–7.6.4 | moderate | investor | GHSA-j3f2-48v5-ccww (бесконечный цикл) |

**Как чинить:** `npm audit fix` в обоих репо, затем `npm ci && npm run build` для проверки. `body-parser` и `protobufjs` приходят транзитивно из **неиспользуемых** зависимостей — см. S-16, их проще удалить, чем чинить.

### M-14. Деплой на прод-сервер по SSH с паролем
**Файлы:** `.github/workflows/deploy.yml` (backend), `atria-admin-dashboard/.github/workflows/ci-deploy.yml`, `atria-investor-dashboard/.github/workflows/deploy.yml`

Везде `password: ${{ secrets.DEPLOY_SSH_PASSWORD }}`. У бэкенда рядом есть и `key: ${{ secrets.DEPLOY_SSH_KEY }}` — то есть оба способа сразу; у панелей только пароль. Пароль SSH на прод-хосте, который через тот же канал получает `POSTGRES_PASSWORD`, `NIKITA_PRO_API_KEY`, `DIDIT_API_KEY`, — это самая слабая точка всей цепочки.

**Как чинить:**
1. Только ключи. В `sshd_config`: `PasswordAuthentication no`, `PermitRootLogin no`.
2. Отдельный deploy-пользователь без sudo на всё; `docker compose` через группу `docker` или sudoers-правило на одну команду.
3. Ещё лучше — уйти с «SSH + tar» на push образа в registry и `docker compose pull` на сервере, тогда исходники и secrets вообще не едут по SSH.
4. Пиннить actions по SHA (`actions/checkout@<sha>`), а не по подвижным тегам.

### M-15. `POST /appeals` — анонимная незалимитированная запись в БД
**Файл:** `src/Atria.Api/Controllers/AppealsController.cs:29-38`

Эндпоинт анонимен по делу (забаненный пользователь не имеет токена), но: нет rate limiting (в `throttledPaths` его нет), нет капчи, нет дедупликации, `Message` пишется в БД. Любой может залить неограниченное число записей — засорить очередь SuperAdmin'а и раздуть таблицу.

**Как чинить:** добавить `/api/v1/appeals` в `throttledPaths` (2–3 запроса в час на IP), ограничить длину `Message` валидатором (проверить, есть ли `SubmitAppealCommandValidator`), и дедуплицировать по `(username, hash(message))` за сутки.

---

## 🟡 Маленькие

| # | Файл | Проблема | Фикс |
|---|---|---|---|
| S-1 | `Program.cs:120-133` | В `TokenValidationParameters` не задан `ValidAlgorithms`. С симметричным ключом `alg:none`/RS256-confusion и так не проходят, но это защита-в-глубину | `ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }` |
| S-2 | `appsettings.json:12` | `AllowedHosts: "*"` — Host-header атаки, отравление ссылок | Перечислить реальные хосты |
| S-3 | `DependencyInjection.cs:256-262` | `AddSingleton<ITokenGateway>` резолвит `IBlockchainSigner` (transient typed HttpClient) из корневого провайдера — captive dependency, `HttpMessageHandler` живёт вечно, ротация DNS ломается | Сделать `ITokenGateway` scoped, либо инжектить `IHttpClientFactory` |
| S-4 | `DependencyInjection.cs:180, 196, 264` | Ни на одном `HttpClient` не задан `Timeout` и нет Polly (retry/circuit breaker). Зависший Didit/SMS/signer держит поток 100 с | `client.Timeout = TimeSpan.FromSeconds(10)` + `AddStandardResilienceHandler()` |
| S-5 | `AesGcmEncryptionService.cs:38` | Шифрование без AAD и без версии ключа: шифротекст из одной колонки можно переставить в другую, ротация ключа невозможна | Добавить AAD (`table:column:rowId`) и однобайтовый префикс версии ключа |
| S-6 | `OtpService.cs:76, 88` | В лог пишется полный номер телефона (`{Phone}`) — PII в логах вопреки комментарию в `Program.cs:23` | Логировать маску `+996***456` или хэш |
| S-7 | `JwtTokenGenerator.cs:40` | В payload JWT кладётся `email`, хотя продукт phone-first и почты нет. Лишний PII в токене, который лежит в `localStorage` | Убрать claim, `ICurrentUserService.Email` читать из БД при надобности |
| S-8 | `AtriaPropertyToken.sol:184-197` | `forcedTransfer` не проверяет `frozen[to]` — можно перевести доли на замороженный адрес, откуда их нельзя двинуть | Либо `require(!frozen[to])`, либо задокументировать как намеренное |
| S-9 | `AtriaPropertyToken.sol:202-214` | `reportCollateral` не валидирует ничего: `valuation == 0`, `valuedAt` в будущем, `uri` любой длины | `require(valuation > 0)`, `require(valuedAt <= block.timestamp)`, ограничить длину `uri` |
| S-10 | `AtriaPropertyToken.sol:163` | `unfreeze` не проверяет `account != address(0)` (в отличие от `freeze`) — асимметрия | Добавить проверку для консистентности |
| S-11 | `IdentityRegistry.sol:219` | `deactivateIssuer` эмитит `IssuerRegistered(..., active: false)` — индексатор, слушающий «регистрацию», получит деактивацию | Завести отдельное событие `IssuerDeactivated` |
| S-12 | `EvmTokenGateway.cs:64, 105` | `CancellationToken ct` принимается, но в `SendRequestAndWaitForReceiptAsync` не передаётся — отмена не работает, ожидание receipt'а может висеть бесконечно | Передать `ct` в перегрузку с `CancellationTokenSource` |
| S-13 | `RefreshTokenStore.cs` | Нет очистки протухших/отозванных токенов — таблица растёт монотонно | Фоновая задача: удалять `ExpiresAtUtc < now - 30d` |
| S-14 | `RefreshTokenCommandHandler.cs:46-73` | Нет оптимистичной блокировки на `RefreshTokens`: два одновременных refresh с одним токеном могут оба пройти проверку `IsRevoked` | Добавить `rowversion`/`xmin` concurrency token или `UPDATE ... WHERE is_revoked = false` с проверкой затронутых строк |
| S-15 | `Dockerfile`, `docker-compose.yml` | Есть `USER $APP_UID` (хорошо), но нет `HEALTHCHECK`, `read_only: true`, `security_opt: [no-new-privileges:true]`, `cap_drop: [ALL]`, `mem_limit`/`cpus`. Базовый образ не пиннится по digest (CIS Docker 4.1, 5.3, 5.10, 5.12, 5.25) | Добавить перечисленное в compose; `FROM ...aspnet:9.0@sha256:...` |
| S-16 | обе панели, `package.json` | `@google/genai` и `dotenv` не импортируются нигде (проверил `git grep` по всем tracked-файлам); `express` не используется в investor. Это лишняя supply-chain поверхность и источник транзитивных CVE из M-13 | `npm uninstall @google/genai dotenv` (+ `express` в investor) |
| S-17 | `Program.cs:205` | 429 отдаётся без `Retry-After` — клиент не знает, когда повторять | `options.OnRejected` с записью заголовка из метаданных лимитера |
| S-18 | `atria-contracts/cache/invariant/failures/…` | Локальный файл с записанным контрпримером к `invariant_theComplianceOverrideNeverLeaks` от 4 авг. Сейчас `forge test` зелёный (46/46), `cache/` в `.gitignore` — то есть это артефакт промежуточной итерации, не живой баг | Почистить `forge clean`, чтобы не путал |

---

## Что проверено и оказалось в порядке

Чтобы было видно границы ревью — это проверялось целенаправленно и претензий не вызвало:

- **BOLA / IDOR (OWASP API1:2023).** Прошёл по всем investor-ресурсам: `GetInvestmentByIdQueryHandler.cs:38`, `GetInvestmentChainRecordQueryHandler.cs:59`, `CancelInvestmentCommand.cs:42`, `WithdrawInvestmentCommand.cs:84`, `GetDocumentByIdQuery.cs:46`, `GetMyDocumentsQuery.cs:26` — везде сверка с `_currentUser.UserId` и «чужое = 404», а не 403 (правильно: существование строки не утекает). Ролевые атрибуты на 26 контроллерах расставлены последовательно. *Замечание на будущее:* проверка делается руками в каждом хендлере — новый хендлер, где её забудут, никем не поймается. Стоит завести конвенцию + тест-обход всех `IRequestHandler`, принимающих `Guid Id`.
- **Верификация вебхуков Didit** (`DiditKycProvider.cs:215-243`) — HMAC-SHA256 по сырому телу, окно свежести 300 с, `CryptographicOperations.FixedTimeEquals`, fail-closed при отсутствии секрета, идемпотентность по `event_id`. Сделано правильно.
- **Ротация refresh-токенов с reuse-detection** (`RefreshTokenCommandHandler.cs`) — повторное предъявление отозванного токена гасит всю сессию.
- **OTP**: коды хранятся хэшированными, одноразовые, TTL 5 мин, лимит попыток 5, `RandomNumberGenerator.GetInt32` без смещения, магического кода в коде нет и это явно задокументировано (`OtpOptions.cs`).
- **AES-256-GCM** (`AesGcmEncryptionService.cs`) — случайный 96-битный nonce на каждое шифрование, полный 128-битный тег, корректный layout.
- **XSS**: единственный `dangerouslySetInnerHTML` (`DealsView.jsx:741`) — статический print-CSS без пользовательских данных. `eval`/`new Function`/`innerHTML` не встречаются. React 19 экранирует по умолчанию.
- **Mass assignment**: CQRS с явными `record`-командами, доменные сущности к байндеру не подставляются.
- **Секреты в git**: `.env` в обоих `.gitignore` и не отслеживается; в `atria-contracts` гигиена ключей образцовая (`.env.example` с рекомендацией keystore/Ledger, `DEPLOYER_PRIVATE_KEY` помечен testnet-only).
- **Селекторы `mint`/`reportCollateral`** в `CustodyTokenGateway` сверены с `methodIdentifiers` из артефактов Foundry — совпадают.
- **`_complianceOverride`** в `AtriaPropertyToken` — внешних вызовов при поднятом флаге нет, порядок проверок в `forcedTransfer` верный (аллоулист проверяется **до** взведения флага), покрыто stateful-инвариантом.
- **Зависимости .NET** — уязвимых пакетов нет.

---

## Порядок работ

1. **Сейчас:** C-3 (5 строк, закрывает брутфорс SuperAdmin), M-1 (удалить строку).
2. **На этой неделе:** C-2 (guard + провернуть секреты), C-1 (правка контракта — потребует передеплоя `IdentityRegistry`, лучше до мейннета), M-4.
3. **Следующий спринт:** M-2/M-3 (cookie + гонка refresh), M-5, M-6, M-12, M-13.
4. **Инфраструктура:** M-7, M-10, M-14, M-11, S-15.
5. **Хвост:** S-1…S-18 по мере касания соответствующих файлов.

C-1 стоит закрыть **до аудита у внешнего аудитора** — иначе это первое, что он выпишет.


---

# Что сделано (4 августа 2026)

Ниже — только суть правки по каждой находке; подробное обоснование живёт в комментариях к самому коду.

## Критические

**C-1 — захват DID.** `registerDid` теперь `onlyAuthority` ([IdentityRegistry.sol](../atria-contracts/src/IdentityRegistry.sol)).
Подпись контроллера сохранена как второй фактор — authority не может привязать DID к ключу, который
не дал согласия. Добавлен путь восстановления `reassignAnchor(didHash, newController, signature)`:
захваченный или потерянный анкер больше не бетонная стена, при передаче бампается revocation-эпоха.
Девять новых тестов в [test/IdentityRegistry.t.sol](../atria-contracts/test/IdentityRegistry.t.sol), включая
`test_strangerCannotRegisterSomeoneElsesDid`.

**C-2 — секреты в конфиге.** Все значения в `appsettings.json` обнулены. Добавлен
[SecretsGuard](src/Atria.Infrastructure/Configuration/SecretsGuard.cs): вне Development процесс **отказывается
стартовать**, если секрет пуст, короче нужного или совпадает (по SHA-256) с ранее закоммиченным.
`Jwt:SigningKey` и `Otp:HashPepper` проверяются новым `[Base256BitKey]` — требуется base64 от 32
случайных байт, а не «32 символа». `DiditKycProvider.VerifySignature` больше не считает `CHANGE_ME`
настроенным секретом. Guard сразу поймал тестовый хост — тестовая фабрика теперь конфигурируется
через env, как прод, а не в обход проверки.

**C-3 — брутфорс логинов.** В `throttledPaths` внесены реально существующие маршруты
(`/auth/admin/login`, `/auth/realtor/login`, `/auth/refresh`, `/auth/register`, `/appeals`).
Добавлена блокировка **учётки**, а не только адреса: `User.RegisterFailedLogin` + `IAuthLockoutPolicy`
(10 попыток → 15 минут, настраивается через `Auth:Lockout`). Заблокированная учётка отвергается
**до** проверки BCrypt, иначе локаут остаётся усилителем нагрузки. Тесты:
[AuthHardeningTests](tests/Atria.Api.IntegrationTests/AuthHardeningTests.cs) — в том числе проверка, что
каждый throttled-маршрут вообще существует.

## Средние

| # | Что сделано |
|---|---|
| M-1 | `Otp__MagicCode` удалён из прод-compose вместе с объяснением, почему такой рычаг не появится снова |
| M-2 | Refresh-токен уехал в `HttpOnly; Secure; SameSite=None` cookie ([RefreshTokenCookie](src/Atria.Api/Security/RefreshTokenCookie.cs)), scope `/api/v1/auth`. Access-токен — только в памяти. Добавлен `POST /auth/logout`, гасящий токен на сервере. Обе панели восстанавливают сессию через `restoreSession()` |
| M-3 | `refreshOnce()` очищает слот только в инициаторе; конкурентные 401 больше не гонят второй refresh протухшим токеном и не вызывают ложный «leak → revoke all» |
| M-4 | Запрос к custody подписывается HMAC-SHA256 над `timestamp.nonce.body` (`Blockchain:SignerSharedSecret`), тело сериализуется один раз — подписывается ровно то, что уходит |
| M-5 | OTP-хэш переведён с BCrypt cost 12 на `HMAC-SHA256(pepper, phone\|code)`. Анонимный эндпоинт больше не стоит 250 мс CPU за запрос; телефон вбит в хэш, чтобы строку нельзя было переиграть на другой номер |
| M-6 | IP теперь действительно используется: колонка `otp_codes.requested_from_ip`, лимиты `RequestsPerHourPerIp` и `DistinctPhonesPerHourPerIp` |
| M-7 | Slither и Gitleaks добавлены в CI контрактов (`fail-on: medium`) |
| M-8 | Двухшаговая передача прав в `Allowlist` (`pendingOwner`/`acceptOwnership`) и `IdentityRegistry` (`pendingAuthority`/`acceptAuthority`). Аллоулист теперь деплоится сразу на нужного владельца — деплойер не владеет им ни одного блока |
| M-9 | `Deploy.s.sol._requireRoleSeparation` падает **до** броадкаста при пересечении ролей; переиспользование чужого аллоулиста явно логирует, что `setAgent` не вызван |
| M-10 | Настоящий ESLint вместо `echo`; `npm run audit` и Gitleaks во всех трёх CI; `dotnet list package --vulnerable` в бэкенде |
| M-11 | [ContractSelectorTests](tests/Atria.Application.Tests/Compliance/ContractSelectorTests.cs) пересчитывает keccak-селекторы из сигнатур и ловит появление новых захардкоженных |
| M-12 | `User.SecurityStamp` + [SecurityStampValidator](src/Atria.Api/Security/SecurityStampValidator.cs): бан, деактивация и смена пароля обрывают уже выданные access-токены немедленно |
| M-13 | `npm audit fix` в обеих панелях — 0 уязвимостей |
| M-14 | Пароль SSH убран из всех трёх деплоев, остались только ключи |
| M-15 | `/api/v1/appeals` внесён в throttled-маршруты |

## Маленькие

S-1 `ValidAlgorithms = [HS256]` · S-2 `AllowedHosts` — конкретный список · S-3 `ITokenGateway` стал
scoped (снят captive dependency) · S-4 таймауты на всех HttpClient · S-6 телефоны в логах
маскируются (`+996***456`) · S-7 email-claim убран из JWT и из `ICurrentUserService` ·
S-8 `forcedTransfer` отвергает замороженного получателя · S-9 `reportCollateral` валидирует hash,
оценку, дату и длину URI · S-10 `unfreeze` проверяет нулевой адрес · S-11 отдельное событие
`IssuerDeactivated` · S-12 `CancellationToken` доходит до ожидания receipt'а · S-13 фоновая уборка
протухших refresh-токенов · S-14 ротация через compare-and-set вместо read-then-write ·
S-15 docker: `read_only`, `no-new-privileges`, `cap_drop: ALL`, лимиты, `HEALTHCHECK` ·
S-16 удалены `@google/genai`, `dotenv`, `express` · S-17 заголовок `Retry-After` в 429 ·
S-18 кэш Foundry очищен.

---

# Что осталось

Две вещи сознательно сделаны не до конца — это выбор, а не пропуск:

1. **S-5 (AES-GCM без AAD).** Версионирование ключа и AAD требуют протаскивания контекста строки
   через EF value converters, а сам переход делает нечитаемыми уже зашифрованные записи — нужна
   миграция данных с двойным чтением. Реализация nonce/тега проверена и корректна; сделано только
   то, что не ломает существующие данные. Отдельная задача.

2. **M-10, легаси-предупреждения линтера.** ESLint включён по-настоящему и падает на баг-классе
   (`no-undef`, `rules-of-hooks`). Гигиена — 77 неиспользуемых переменных и мёртвых обработчиков,
   накопленных до ревью, — оставлена предупреждениями. Настоящих багов среди них нет (`no-undef`
   не сработал ни разу). Чистить их в рамках security-правок значило бы смешать два несвязанных
   изменения в одном диффе; после расчистки правило поднимается до `error`.

**Требует действий на вашей стороне (код готов, нужны значения):**
- Сгенерировать и положить в секреты `JWT_SIGNING_KEY`, `ENCRYPTION_KEY`, `OTP_HASH_PEPPER`
  (`openssl rand -base64 32` каждый) и `Blockchain__SignerSharedSecret`. Без них прод не стартует —
  это и есть смысл guard'а.
- Провернуть секреты, которые могли уехать в прод со старыми значениями из git.
- Перевести деплой-хост на `PasswordAuthentication no` и завести `DEPLOY_SSH_KEY`.
- Передеплоить `IdentityRegistry` (C-1 меняет ABI) и перевести бэкенд на новый адрес.
- Custody-подписант должен начать проверять заголовки `X-Atria-Signature/Timestamp/Nonce`.
