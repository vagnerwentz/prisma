using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text.RegularExpressions;

namespace Prisma.Api.Infrastructure.Email;

// O que muda de um e-mail para outro; o resto é o layout (docs/email.md, seção 4). Parágrafos e nota são texto puro:
// **assim** vira negrito, [texto](https://…) vira link, e todo o resto é escapado.
public sealed record EmailTemplate(
    string Subject,
    string Preheader,
    string Eyebrow,
    string Title,
    IReadOnlyList<string> Paragraphs,
    EmailButton? Button = null,
    IReadOnlyList<EmailDetail>? Details = null,
    string? Note = null);

public sealed record EmailButton(string Label, Uri Url, bool ShowLinkFallback = true);

public sealed record EmailDetail(string Label, string Value);

// O layout dos e-mails do Prisma Money, com a identidade do app (CLAUDE.md, 7.1): cabeçalho em tinta com o logo, a
// faixa do espectro logo abaixo, papel quente em volta, título serifado, botão em tinta, quadro de dados e nota.
//
// Feito para leitor de e-mail, não para navegador: tabelas, estilos em linha (o Gmail descarta boa parte do <style>),
// cores em hexadecimal (nada de oklch nem gradiente, que o Outlook não desenha). A faixa do espectro são células
// coloridas, e não imagem: aparece mesmo com as imagens bloqueadas. A única imagem é o logo, servido pelo próprio
// domínio e igual para todos (não identifica quem abriu). O <style> só acrescenta o modo escuro (Apple Mail, Outlook
// do celular) e o ajuste para tela estreita; sem ele, o e-mail continua inteiro.
public static partial class EmailLayout
{
    public const string LogoPath = "email/prisma-logo.png";

    // Tokens do app (index.css), convertidos de oklch.
    private const string Paper = "#fcfaf6";
    private const string Card = "#ffffff";
    private const string Ink = "#171723";
    private const string Muted = "#62626f";
    private const string Surface = "#f2f0ec";
    private const string Rule = "#e5e3dd";
    private const string Serif = "Georgia, 'Times New Roman', serif";
    private const string Sans = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif";

    // As faixas do PrismLogo, na mesma ordem.
    private static readonly string[] Spectrum = ["#8b5cf6", "#3b82f6", "#22d3ee", "#84cc16", "#f59e0b", "#f43f5e"];

    public static EmailContent Render(EmailTemplate email, Uri publicUrl) =>
        new(email.Subject, Html(email, publicUrl), Text(email, publicUrl));

    private static string Html(EmailTemplate email, Uri publicUrl)
    {
        var logo = new Uri(publicUrl, LogoPath);
        var html = new StringBuilder();
        html.Append($$"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <meta name="supported-color-schemes" content="light dark">
            <title>{{E(email.Subject)}}</title>
            <style>
              @media (prefers-color-scheme: dark) {
                .p-paper { background-color: #0a0a14 !important; }
                .p-card { background-color: #141320 !important; }
                .p-header { background-color: #231d3d !important; }
                .p-text { color: #efeff7 !important; }
                .p-muted { color: #9c9dae !important; }
                .p-surface { background-color: #1c1c27 !important; }
                .p-rule { border-color: #2a2936 !important; }
                .p-button { background-color: #efeff7 !important; }
                .p-button a { color: #171723 !important; }
                .p-link { color: #c4b5fd !important; }
              }
              @media (max-width: 600px) {
                .p-pad { padding-left: 24px !important; padding-right: 24px !important; }
                .p-title { font-size: 26px !important; }
                .p-label { display: block !important; padding: 12px 0 2px !important; }
                .p-value { display: block !important; padding: 0 0 12px !important; border-top: 0 !important; text-align: left !important; }
              }
            </style>
            </head>
            <body class="p-paper" style="margin:0;padding:0;background-color:{{Paper}};-webkit-text-size-adjust:100%;">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;mso-hide:all;">{{E(email.Preheader)}}{{string.Concat(Enumerable.Repeat("&#847;&zwnj;&nbsp;", 40))}}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="p-paper" style="background-color:{{Paper}};">
            <tr><td align="center" style="padding:32px 12px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="p-card" style="max-width:560px;background-color:{{Card}};border-radius:16px;overflow:hidden;">
            <tr><td class="p-pad p-header" style="background-color:{{Ink}};padding:26px 40px;">
              <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                <td style="padding-right:12px;vertical-align:middle;"><img src="{{E(logo.ToString())}}" width="36" height="36" alt="" style="display:block;border:0;outline:none;"></td>
                <td style="vertical-align:middle;font-family:{{Serif}};font-size:24px;line-height:1;color:#ffffff;">Prisma Money</td>
              </tr></table>
            </td></tr>
            <tr><td style="font-size:0;line-height:0;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
            """);
        foreach (var color in Spectrum)
            html.Append($"""<td height="4" style="height:4px;background-color:{color};font-size:0;line-height:0;">&nbsp;</td>""");
        html.Append($$"""
              </tr></table>
            </td></tr>
            <tr><td class="p-pad" style="padding:36px 40px 8px;font-family:{{Sans}};">
              <p class="p-muted" style="margin:0 0 10px;font-size:13px;line-height:1.4;color:{{Muted}};">{{E(email.Eyebrow)}}</p>
              <h1 class="p-text p-title" style="margin:0 0 20px;font-family:{{Serif}};font-size:30px;line-height:1.15;font-weight:400;color:{{Ink}};">{{E(email.Title)}}</h1>
            """);
        foreach (var paragraph in email.Paragraphs)
            html.Append($"""<p class="p-text" style="margin:0 0 16px;font-size:16px;line-height:1.6;color:{Ink};">{Rich(paragraph)}</p>""");

        if (email.Button is { } button)
        {
            var url = E(button.Url.ToString());
            html.Append($$"""
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:28px 0 8px;"><tr>
                  <td class="p-button" style="background-color:{{Ink}};border-radius:12px;">
                    <a href="{{url}}" target="_blank" style="display:inline-block;padding:15px 28px;font-family:{{Sans}};font-size:16px;font-weight:600;line-height:1;color:#ffffff;text-decoration:none;border-radius:12px;">{{E(button.Label)}}</a>
                  </td>
                </tr></table>
                """);
            if (button.ShowLinkFallback)
                html.Append($$"""
                    <p class="p-muted" style="margin:20px 0 4px;font-size:13px;line-height:1.5;color:{{Muted}};">Se o botão não abrir, copie este endereço no navegador:</p>
                    <p style="margin:0 0 8px;font-size:13px;line-height:1.5;word-break:break-all;"><a class="p-link" href="{{url}}" target="_blank" style="color:#5f3ed8;text-decoration:underline;">{{url}}</a></p>
                    """);
        }

        if (email.Details is { Count: > 0 } details)
        {
            html.Append($"""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:24px 0 8px;border-collapse:collapse;">""");
            foreach (var detail in details)
                html.Append($$"""
                    <tr>
                      <td class="p-muted p-rule p-label" style="padding:12px 16px 12px 0;border-top:1px solid {{Rule}};font-size:14px;line-height:1.4;color:{{Muted}};white-space:nowrap;vertical-align:top;">{{E(detail.Label)}}</td>
                      <td class="p-text p-rule p-value" style="padding:12px 0;border-top:1px solid {{Rule}};font-size:14px;line-height:1.4;color:{{Ink}};text-align:right;overflow-wrap:anywhere;">{{E(detail.Value)}}</td>
                    </tr>
                    """);
            html.Append("</table>");
        }

        if (email.Note is { } note)
            html.Append($$"""
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:24px 0 8px;"><tr>
                  <td class="p-surface p-text" style="background-color:{{Surface}};border-radius:12px;padding:16px 20px;font-size:14px;line-height:1.55;color:{{Ink}};">{{Rich(note)}}</td>
                </tr></table>
                """);

        html.Append($$"""
            </td></tr>
            <tr><td class="p-pad" style="padding:24px 40px 32px;font-family:{{Sans}};">
              <p class="p-muted p-rule" style="margin:0;padding-top:20px;border-top:1px solid {{Rule}};font-size:12px;line-height:1.6;color:{{Muted}};">E-mail automático do Prisma Money.</p>
            </td></tr>
            </table>
            <p class="p-muted" style="margin:20px 0 0;font-family:{{Sans}};font-size:12px;line-height:1.6;color:{{Muted}};">Prisma Money · <a class="p-muted" href="{{E(publicUrl.ToString())}}" style="color:{{Muted}};text-decoration:none;">{{E(publicUrl.Host)}}</a></p>
            </td></tr>
            </table>
            </body>
            </html>
            """);
        return html.ToString();
    }

    private static string Text(EmailTemplate email, Uri publicUrl)
    {
        var text = new StringBuilder();
        text.AppendLine("Prisma Money").AppendLine();
        text.AppendLine(email.Title).AppendLine();
        foreach (var paragraph in email.Paragraphs)
            text.AppendLine(Plain(paragraph)).AppendLine();
        if (email.Button is { } button)
            text.AppendLine($"{button.Label}: {button.Url}").AppendLine();
        if (email.Details is { Count: > 0 } details)
        {
            foreach (var detail in details)
                text.AppendLine($"{detail.Label}: {detail.Value}");
            text.AppendLine();
        }

        if (email.Note is { } note)
            text.AppendLine(Plain(note)).AppendLine();
        text.AppendLine("--");
        text.AppendLine("E-mail automático do Prisma Money.");
        text.Append($"Prisma Money · {publicUrl.Host}");
        return text.ToString();
    }

    // Só a marcação é escapada: os acentos ficam como texto, legíveis e menores.
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private static string E(string value) => Encoder.Encode(value);

    // Escapa tudo e só então troca **x** por negrito e [x](url) por link: nada do texto vira outra marcação.
    private static string Rich(string value) =>
        LinkMarkup().Replace(Bold().Replace(E(value), "<strong>$1</strong>"),
            """<a class="p-link" href="$2" target="_blank" style="color:#5f3ed8;text-decoration:underline;">$1</a>""");

    private static string Plain(string value) => LinkMarkup().Replace(value.Replace("**", ""), "$1 ($2)");

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"\[([^\]]+)\]\((https?://[^)\s]+)\)")]
    private static partial Regex LinkMarkup();
}
