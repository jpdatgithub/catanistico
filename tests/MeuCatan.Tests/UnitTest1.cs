using MeuCatan.MudblazorWasmClient.Components.Catan;

namespace MeuCatan.Tests;

public class CatanBoardLayoutBuilderTests
{
    [Fact]
    public void BuildHexagonPointsTest()
    {
        Assert.Equal("536.6,300 450,250 363.4,300 363.4,400 450,450 536.6,400", CatanBoardLayoutBuilder.CalcularPontosSvg(450, 350, 100));
    }
}
