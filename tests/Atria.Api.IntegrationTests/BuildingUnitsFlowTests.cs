using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Confirms the admin flow the object form is built on: create a building, fill it with units
/// (apartment + garage) that each carry their own token issue and room breakdown, read them back
/// through the building, and refuse to delete a building that still holds units.
/// </summary>
public sealed class BuildingUnitsFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string BuildingsRoute = "/api/v1/buildings";
    private const string PropertiesRoute = "/api/v1/properties";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";

    private readonly AtriaApiFactory _factory;

    public BuildingUnitsFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Building_HoldsManyUnits_EachWithItsOwnIssueAndRooms()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var buildingId = await CreateBuildingAsync(admin);

        // Квартира: 3-комнатная, 128,82 м², со своим выпуском токенов и разбивкой по комнатам.
        var apartmentId = await CreateUnitAsync(admin, new
        {
            name = "3-комнатный апартамент №12",
            totalValue = 12_882_000m,
            tokenPrice = 1_000m,
            totalTokens = 12_882L,
            currency = "KGS",
            buildingId,
            unitType = "apartment",
            unitNumber = "12",
            floorNumber = 4,
            roomCount = 3,
            totalAreaSqM = 128.82m,
            rooms = new object[]
            {
                new { name = "Кухня+Столовая", areaSqM = 28.68m },
                new { name = "Прихожая", areaSqM = 5.65m },
                new { name = "Ванная", areaSqM = 4.67m },
                new { name = "Спальня", areaSqM = 14.88m },
                new { name = "Лоджия", areaSqM = 3.67m },
            },
        });

        // Гараж в том же здании — отдельный выпуск, без комнат.
        var garageId = await CreateUnitAsync(admin, new
        {
            name = "Гараж Г-4",
            totalValue = 900_000m,
            tokenPrice = 500m,
            totalTokens = 1_800L,
            currency = "KGS",
            buildingId,
            unitType = "garage",
            unitNumber = "Г-4",
            floorNumber = -1,
            totalAreaSqM = 18.4m,
        });

        var apartment = await GetJsonAsync(admin, $"{PropertiesRoute}/{apartmentId}");
        apartment.GetProperty("buildingId").GetString().Should().Be(buildingId);
        apartment.GetProperty("unitType").GetString().Should().Be("apartment");
        apartment.GetProperty("unitNumber").GetString().Should().Be("12");
        apartment.GetProperty("floorNumber").GetInt32().Should().Be(4);
        apartment.GetProperty("roomCount").GetInt32().Should().Be(3);
        apartment.GetProperty("totalAreaSqM").GetDecimal().Should().Be(128.82m);
        apartment.GetProperty("totalTokens").GetInt64().Should().Be(12_882L);

        // Разбивка приходит в том порядке, в котором её ввёл админ, с суммой для сверки с площадью.
        apartment.GetProperty("rooms").EnumerateArray().Select(r => r.GetProperty("name").GetString())
            .Should().ContainInOrder("Кухня+Столовая", "Прихожая", "Ванная", "Спальня", "Лоджия");
        apartment.GetProperty("roomsAreaSqM").GetDecimal().Should().Be(57.55m);

        var garage = await GetJsonAsync(admin, $"{PropertiesRoute}/{garageId}");
        garage.GetProperty("unitType").GetString().Should().Be("garage");
        garage.GetProperty("totalTokens").GetInt64().Should().Be(1_800L, "each unit issues its own tokens");
        garage.GetProperty("rooms").GetArrayLength().Should().Be(0);

        // Здание отдаёт оба юнита разом — это и есть карточка объекта в админке.
        var building = await GetJsonAsync(admin, $"{BuildingsRoute}/{buildingId}");
        building.GetProperty("unitCount").GetInt32().Should().Be(2);
        building.GetProperty("units").EnumerateArray().Select(u => u.GetProperty("id").GetString())
            .Should().BeEquivalentTo(new[] { apartmentId, garageId });
    }

    [Fact]
    public async Task A_building_whose_units_are_all_drafts_is_invisible_to_the_public()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var anon = _factory.CreateClient();

        // Здание заведено, помещения ещё черновики — на сайте его быть не должно вообще.
        var buildingId = await CreateBuildingAsync(admin);
        await CreateUnitAsync(admin, MinimalUnit(buildingId, "Апартамент 1"));

        var anonList = await GetJsonAsync(anon, BuildingsRoute);
        anonList.EnumerateArray().Select(b => b.GetProperty("id").GetString())
            .Should().NotContain(buildingId, "черновик не выкладывается на публику даже пустой карточкой");

        // И по прямой ссылке тоже: существование объекта не подтверждается.
        (await anon.GetAsync($"{BuildingsRoute}/{buildingId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Админ видит его всегда — ему с ним работать.
        (await GetJsonAsync(admin, BuildingsRoute)).EnumerateArray()
            .Select(b => b.GetProperty("id").GetString()).Should().Contain(buildingId);
        (await admin.GetAsync($"{BuildingsRoute}/{buildingId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_building_with_no_units_at_all_is_invisible_to_the_public()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var anon = _factory.CreateClient();

        var buildingId = await CreateBuildingAsync(admin);

        (await GetJsonAsync(anon, BuildingsRoute)).EnumerateArray()
            .Select(b => b.GetProperty("id").GetString())
            .Should().NotContain(buildingId, "пустая карточка — не листинг");
        (await anon.GetAsync($"{BuildingsRoute}/{buildingId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_building_appears_publicly_as_soon_as_one_unit_goes_on_sale()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var anon = _factory.CreateClient();

        var buildingId = await CreateBuildingAsync(admin);
        var draftId = await CreateUnitAsync(admin, MinimalUnit(buildingId, "Апартамент 1"));
        var openId = await CreateUnitAsync(admin, MinimalUnit(buildingId, "Апартамент 2"));

        (await anon.GetAsync($"{BuildingsRoute}/{buildingId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "пока всё в черновиках — здания нет");

        await GovernanceTestHelpers.PublishAsync(_factory, admin, openId);

        var building = await GetJsonAsync(anon, $"{BuildingsRoute}/{buildingId}");
        building.GetProperty("unitCount").GetInt32().Should().Be(1);
        building.GetProperty("units").EnumerateArray().Select(u => u.GetProperty("id").GetString())
            .Should().Contain(openId).And.NotContain(draftId, "опубликовано одно помещение, а не всё здание");
    }

    [Fact]
    public async Task Units_AreDraftsUntilPublished_AndHiddenFromThePublicBuilding()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var anon = _factory.CreateClient();

        var buildingId = await CreateBuildingAsync(admin);
        var draftId = await CreateUnitAsync(admin, MinimalUnit(buildingId, "Апартамент 1"));
        var openId = await CreateUnitAsync(admin, MinimalUnit(buildingId, "Апартамент 2"));
        await GovernanceTestHelpers.PublishAsync(_factory, admin, openId);

        // Здание видно всем, но черновой юнит в публичной выдаче не появляется.
        var publicBuilding = await GetJsonAsync(anon, $"{BuildingsRoute}/{buildingId}");
        var publicUnitIds = publicBuilding.GetProperty("units").EnumerateArray()
            .Select(u => u.GetProperty("id").GetString()).ToList();
        publicUnitIds.Should().Contain(openId).And.NotContain(draftId);
        publicBuilding.GetProperty("unitCount").GetInt32().Should().Be(1);

        // Админ видит оба.
        var adminBuilding = await GetJsonAsync(admin, $"{BuildingsRoute}/{buildingId}");
        adminBuilding.GetProperty("unitCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Update_ReplacesTheRoomBreakdown_AndAnEmptyListClearsIt()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var buildingId = await CreateBuildingAsync(admin);
        var unitId = await CreateUnitAsync(admin, new
        {
            name = "2-комнатный апартамент",
            totalValue = 5_755_000m,
            tokenPrice = 1_000m,
            totalTokens = 5_755L,
            currency = "KGS",
            buildingId,
            unitType = "apartment",
            roomCount = 2,
            totalAreaSqM = 57.55m,
            rooms = new object[] { new { name = "Кухня", areaSqM = 12m } },
        });

        // Присланный список заменяет прежний целиком.
        (await admin.PatchAsJsonAsync($"{PropertiesRoute}/{unitId}", new
        {
            rooms = new object[]
            {
                new { name = "Кухня+Столовая", areaSqM = 28.68m },
                new { name = "Спальня", areaSqM = 14.88m },
            },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterReplace = await GetJsonAsync(admin, $"{PropertiesRoute}/{unitId}");
        afterReplace.GetProperty("rooms").EnumerateArray().Select(r => r.GetProperty("name").GetString())
            .Should().ContainInOrder("Кухня+Столовая", "Спальня");
        afterReplace.GetProperty("roomCount").GetInt32().Should().Be(2, "an untouched field survives the PATCH");

        // Пустой список очищает разбивку.
        (await admin.PatchAsJsonAsync($"{PropertiesRoute}/{unitId}", new { rooms = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetJsonAsync(admin, $"{PropertiesRoute}/{unitId}"))
            .GetProperty("rooms").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Create_WithAnUnknownBuilding_Is404()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var create = await admin.PostAsJsonAsync(PropertiesRoute, MinimalUnit(Guid.NewGuid().ToString(), "Никуда"));

        create.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RefusesWhileTheBuildingStillHoldsUnits()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var buildingId = await CreateBuildingAsync(admin);
        var unitId = await CreateUnitAsync(admin, MinimalUnit(buildingId, "Апартамент 1"));

        (await admin.DeleteAsync($"{BuildingsRoute}/{buildingId}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "units are live issues, not stray rows");

        // PATCH без buildingId связь не трогает.
        (await admin.PatchAsJsonAsync($"{PropertiesRoute}/{unitId}", new { name = "Апартамент 1" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetJsonAsync(admin, $"{PropertiesRoute}/{unitId}"))
            .GetProperty("buildingId").GetString().Should().Be(buildingId, "a PATCH without buildingId keeps the link");

        // Нулевой Guid — явная отвязка; после неё здание пустое и удаляется.
        (await admin.PatchAsJsonAsync($"{PropertiesRoute}/{unitId}", new { buildingId = Guid.Empty }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetJsonAsync(admin, $"{PropertiesRoute}/{unitId}"))
            .GetProperty("buildingId").ValueKind.Should().Be(JsonValueKind.Null);

        (await admin.DeleteAsync($"{BuildingsRoute}/{buildingId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"{BuildingsRoute}/{buildingId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WritingBuildings_RequiresAdminRole()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var buildingId = await CreateBuildingAsync(admin);

        var anon = _factory.CreateClient();
        (await anon.PostAsJsonAsync(BuildingsRoute, new { name = "Чужое здание" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.DeleteAsync($"{BuildingsRoute}/{buildingId}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);
        (await investor.PostAsJsonAsync(BuildingsRoute, new { name = "Чужое здание" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await investor.PatchAsJsonAsync($"{BuildingsRoute}/{buildingId}", new { name = "Переименовано" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Чтение открыто всем.
        (await anon.GetAsync(BuildingsRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ParkingSpace_KeepsItsSectionRowAndSpot_AcrossCreateReadAndUpdate()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var buildingId = await CreateBuildingAsync(admin);

        // Парковочное место в подземном паркинге: этаж отрицательный, комнат нет вовсе — и то и
        // другое нормально, а не ошибка валидации.
        var spaceId = await CreateUnitAsync(admin, new
        {
            name = "Парковочное место P-125",
            totalValue = 1_000_000m,
            tokenPrice = 1_000m,
            totalTokens = 1_000L,
            minPurchaseTokens = 1L,
            currency = "KGS",
            buildingId,
            unitType = "parking_space",
            unitNumber = "P-125",
            floorNumber = -1,
            section = "B",
            row = "12",
            spot = "125",
            roomCount = (int?)null,
            totalAreaSqM = 15.2m,
            rooms = Array.Empty<object>(),
        });

        // Перезагрузка списка: значения должны вернуться, а не превратиться в «—».
        var space = await GetJsonAsync(admin, $"{PropertiesRoute}/{spaceId}");
        space.GetProperty("unitType").GetString().Should().Be("parking_space");
        space.GetProperty("floorNumber").GetInt32().Should().Be(-1, "подземный паркинг — это минус первый этаж");
        space.GetProperty("section").GetString().Should().Be("B");
        space.GetProperty("row").GetString().Should().Be("12");
        space.GetProperty("spot").GetString().Should().Be("125");

        // Те же поля приходят и внутри здания — карточка гаража берёт их отсюда.
        var building = await GetJsonAsync(admin, $"{BuildingsRoute}/{buildingId}");
        var unit = building.GetProperty("units").EnumerateArray()
            .Single(u => u.GetProperty("id").GetString() == spaceId);
        unit.GetProperty("section").GetString().Should().Be("B");
        unit.GetProperty("row").GetString().Should().Be("12");
        unit.GetProperty("spot").GetString().Should().Be("125");

        // Секция бывает буквой, ряд — с буквой: строки, а не числа.
        (await admin.PatchAsJsonAsync($"{PropertiesRoute}/{spaceId}", new
        {
            section = "Б",
            row = "12А",
            spot = "125",
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        space = await GetJsonAsync(admin, $"{PropertiesRoute}/{spaceId}");
        space.GetProperty("section").GetString().Should().Be("Б");
        space.GetProperty("row").GetString().Should().Be("12А");
    }

    [Fact]
    public async Task SwitchingAwayFromAParkingSpace_ClearsTheParkingAddress()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var buildingId = await CreateBuildingAsync(admin);
        var unitId = await CreateUnitAsync(admin, new
        {
            name = "Парковочное место P-125",
            totalValue = 1_000_000m,
            tokenPrice = 1_000m,
            totalTokens = 1_000L,
            currency = "KGS",
            buildingId,
            unitType = "parking_space",
            unitNumber = "P-125",
            floorNumber = -1,
            section = "B",
            row = "12",
            spot = "125",
        });

        // Админ передумал и сделал юнит квартирой. Форма шлёт три null — адрес паркинга должен
        // стереться, а не залипнуть: квартира не стоит в ряду 12.
        (await admin.PatchAsJsonAsync($"{PropertiesRoute}/{unitId}", new
        {
            unitType = "apartment",
            roomCount = 3,
            section = (string?)null,
            row = (string?)null,
            spot = (string?)null,
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var unit = await GetJsonAsync(admin, $"{PropertiesRoute}/{unitId}");
        unit.GetProperty("unitType").GetString().Should().Be("apartment");
        unit.GetProperty("roomCount").GetInt32().Should().Be(3);
        unit.GetProperty("section").ValueKind.Should().Be(JsonValueKind.Null);
        unit.GetProperty("row").ValueKind.Should().Be(JsonValueKind.Null);
        unit.GetProperty("spot").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static object MinimalUnit(string buildingId, string name) => new
    {
        name,
        totalValue = 1_000_000m,
        tokenPrice = 1_000m,
        totalTokens = 1_000L,
        currency = "KGS",
        buildingId,
        unitType = "apartment",
    };

    private static async Task<string> CreateBuildingAsync(HttpClient adminClient)
    {
        var create = await adminClient.PostAsJsonAsync(BuildingsRoute, new
        {
            name = "ЖК Ала-Тоо, блок B",
            description = "Жилой блок на 9 этажей",
            address = "Эркиндик 12, Бишкек",
            city = "Бишкек",
            developer = "Ala-Too Development",
            yearBuilt = 2019,
            floors = 9,
            buildingType = "residential",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return doc.RootElement.GetString()!;
    }

    private static async Task<string> CreateUnitAsync(HttpClient adminClient, object body)
    {
        var create = await adminClient.PostAsJsonAsync(PropertiesRoute, body);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return doc.RootElement.GetString()!;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string route)
    {
        var response = await client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task AuthenticateAdminAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(AdminLoginRoute, new
        {
            username = "admin",
            password = "admin-test-password",
        });
        login.IsSuccessStatusCode.Should().BeTrue("static admin login should be enabled in tests");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ReadTokenAsync(login));
    }

    private async Task AuthenticateInvestorAsync(HttpClient client)
    {
        var phone = UniqueKgPhone();
        await client.PostAsJsonAsync(RequestOtpRoute, new { phone });
        var verify = await client.PostAsJsonAsync(VerifyOtpRoute, new { phone, code = _factory.Sms.CodeFor(phone) });
        verify.IsSuccessStatusCode.Should().BeTrue();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ReadTokenAsync(verify));
    }

    private static async Task<string?> ReadTokenAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString();
    }

    private static string UniqueKgPhone()
    {
        var digits = new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).ToArray());
        var eight = (digits + "00000000")[..8];
        return $"+9967{eight}";
    }
}
