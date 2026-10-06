using Prisma.Api.Infrastructure.Market;
using Shouldly;

namespace Prisma.Api.Tests.Market;

// docs/investimentos.md, etapa 4: o SVG do logo é limpo antes de ser guardado. Fica o desenho; sai o que executa,
// busca algo de fora ou embute documento.
public sealed class SvgSanitizerTests
{
    // O logo real do BBAS3 na brapi (2026-10-05).
    private const string Bbas3 =
        """<svg xmlns="http://www.w3.org/2000/svg" width="56" height="56"><path fill="#FFF22D" d="M0 0h56v56H0z"/><path fill="#2360A5" d="m12 39.555 3.788-2.221 3.28 2.22L12 44z"/></svg>""";

    private static string Wrap(string inner, string rootAttributes = "") =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="56" height="56"{rootAttributes}>{inner}</svg>""";

    [Fact]
    public void A_clean_logo_keeps_its_drawing()
    {
        var clean = SvgSanitizer.Clean(Bbas3)!;

        clean.ShouldContain("""fill="#FFF22D" d="M0 0h56v56H0z""");
        clean.ShouldContain("""fill="#2360A5""");
        clean.ShouldStartWith("<svg");
    }

    [Theory]
    [InlineData("""<script>alert(1)</script>""", "script")]
    [InlineData("""<foreignObject><div xmlns="http://www.w3.org/1999/xhtml">oi</div></foreignObject>""", "foreignObject")]
    [InlineData("""<image href="https://evil.example/x.png" width="10" height="10"/>""", "image")]
    [InlineData("""<iframe src="https://evil.example"/>""", "iframe")]
    [InlineData("""<animate attributeName="href" to="javascript:alert(1)"/>""", "animate")]
    [InlineData("""<set attributeName="onload" to="alert(1)"/>""", "<set")]
    public void Elements_that_run_or_embed_are_removed(string inner, string forbidden)
    {
        var clean = SvgSanitizer.Clean(Wrap($"""<path d="M0 0h1v1z"/>{inner}"""))!;

        clean.ShouldNotContain(forbidden);
        clean.ShouldContain("""d="M0 0h1v1z""");
    }

    [Fact]
    public void Event_attributes_are_removed_from_any_element_including_the_root()
    {
        var clean = SvgSanitizer.Clean(Wrap("""<path onclick="alert(1)" d="M0 0h1v1z"/>""", """ onload="alert(1)" """))!;

        clean.ShouldNotContain("onload");
        clean.ShouldNotContain("onclick");
        clean.ShouldContain("""d="M0 0h1v1z""");
    }

    [Fact]
    public void Links_stay_only_inside_the_drawing()
    {
        var clean = SvgSanitizer.Clean(Wrap(
            """<defs><linearGradient id="g"/></defs><use xlink:href="#g"/><use href="https://evil.example/x.svg#a"/><a href="javascript:alert(1)"><path d="M0 0"/></a>"""))!;

        clean.ShouldContain("#g");
        clean.ShouldNotContain("evil.example");
        clean.ShouldNotContain("javascript:");
    }

    [Fact]
    public void Paint_may_point_only_to_the_drawing_itself()
    {
        var clean = SvgSanitizer.Clean(Wrap(
            """<path fill="url(#g)" d="M0 0"/><path fill="url(https://evil.example/p.svg#x)" d="M1 1"/><path style="fill:url('http://evil.example')" d="M2 2"/>"""))!;

        clean.ShouldContain("url(#g)");
        clean.ShouldNotContain("evil.example");
    }

    [Fact]
    public void Elements_from_other_namespaces_and_comments_are_removed()
    {
        var clean = SvgSanitizer.Clean(Wrap("""<!-- olá --><x:payload xmlns:x="urn:evil">oi</x:payload><path d="M0 0"/>"""))!;

        clean.ShouldNotContain("payload");
        clean.ShouldNotContain("olá");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("não é xml")]
    [InlineData("""<html><body>404</body></html>""")] // a página de erro do GitHub Pages
    [InlineData("""<svg width="56"><path d="M0 0"/></svg>""")] // sem o namespace do SVG
    [InlineData("""<?xml version="1.0"?><!DOCTYPE svg [<!ENTITY a "aaaa">]><svg xmlns="http://www.w3.org/2000/svg">&a;</svg>""")]
    public void What_is_not_an_svg_is_rejected(string? raw) => SvgSanitizer.Clean(raw).ShouldBeNull();

    [Fact]
    public void Oversized_svg_is_rejected()
    {
        var huge = Wrap($"""<path d="{new string('1', SvgSanitizer.MaxBytes)}"/>""");

        SvgSanitizer.Clean(huge).ShouldBeNull();
    }
}
