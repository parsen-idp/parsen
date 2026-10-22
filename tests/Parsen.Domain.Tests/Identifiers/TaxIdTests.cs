using Parsen.Domain.Identifiers;

namespace Parsen.Domain.Tests.Identifiers;

public sealed class CpfTests
{
    [Theory]
    [InlineData("52998224725")]
    [InlineData("12345678909")]
    [InlineData("529.982.247-25")]
    public void ValidCpf_IsAccepted(string cpf) => Assert.True(Cpf.IsValid(cpf));

    [Theory]
    [InlineData("52998224724")] // último dígito errado
    [InlineData("52998224715")] // penúltimo dígito errado
    [InlineData("11111111111")] // todos iguais passam no cálculo, mas não são CPF
    [InlineData("5299822472")]  // curto
    [InlineData("529982247250")] // longo
    [InlineData("5299822472A")]
    [InlineData("")]
    public void InvalidCpf_IsRefused(string cpf) => Assert.False(Cpf.IsValid(cpf));

    [Fact]
    public void CheckDigits_CompleteTheFirstNineDigits() => Assert.Equal("25", Cpf.CheckDigits("529982247"));

    [Fact]
    public void Normalize_KeepsOnlyDigits() => Assert.Equal("52998224725", Cpf.Normalize(" 529.982.247-25 "));
}

public sealed class CnpjTests
{
    [Theory]
    [InlineData("11222333000181")]
    [InlineData("11.222.333/0001-81")]
    [InlineData("12ABC34501DE35")]     // alfanumérico (exemplo da Receita Federal, julho de 2026)
    [InlineData("12.ABC.345/01DE-35")]
    [InlineData("12abc34501de35")]     // letras minúsculas são normalizadas
    public void ValidCnpj_IsAccepted(string cnpj) => Assert.True(Cnpj.IsValid(cnpj));

    [Theory]
    [InlineData("11222333000182")]
    [InlineData("12ABC34501DE36")]
    [InlineData("00000000000000")]
    [InlineData("1122233300018")]
    [InlineData("12ABC34501DEAB")] // dígitos verificadores são sempre numéricos
    [InlineData("")]
    public void InvalidCnpj_IsRefused(string cnpj) => Assert.False(Cnpj.IsValid(cnpj));

    [Fact]
    public void CheckDigits_WorkForNumericAndAlphanumericBases()
    {
        Assert.Equal("81", Cnpj.CheckDigits("112223330001"));
        Assert.Equal("35", Cnpj.CheckDigits("12ABC34501DE"));
    }

    [Fact]
    public void Normalize_KeepsLettersAndDigitsInUpperCase() => Assert.Equal("12ABC34501DE35", Cnpj.Normalize("12.abc.345/01de-35"));
}
