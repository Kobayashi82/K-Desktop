using KDesktop.Core.Mail;

namespace KDesktop.Core.Tests;

public class MailTextTests
{
    [Fact]
    public void HtmlToTextWithRealDomains()
    {
        const string html = """
            <html><head><style>p{color:red}</style><title>x</title></head><body>
            <p>Hola&nbsp;Ana,</p><p>Tu factura de <b>octubre</b> está lista.</p>
            <ul><li>Importe: 42,10&nbsp;€</li><li>Vence: 5/11</li></ul>
            <p><a href="https://www.paypa1-seguro.com/login">Pagar en tubanco.es</a> · <a href="https://tubanco.es">tubanco.es</a></p>
            <img src="https://track.example.com/pixel.gif" width="1" height="1">
            <script>alert(1)</script><!-- oculto -->
            </body></html>
            """;
        var text = MailText.FromHtml(html);
        Assert.Contains("Hola Ana,", text, StringComparison.Ordinal);
        Assert.Contains("• Importe: 42,10 €", text, StringComparison.Ordinal);
        Assert.Contains("Pagar en tubanco.es (paypa1-seguro.com)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tubanco.es (tubanco.es)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", text, StringComparison.Ordinal);
        Assert.DoesNotContain("color:red", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pixel", text, StringComparison.Ordinal);
        Assert.DoesNotContain("oculto", text, StringComparison.Ordinal);
        Assert.Null(MailText.Domain("javascript:alert(1)"));
    }

    [Theory]
    [InlineData("Vale, mañana a las 10.\n\nEl lun, 2 nov 2026 a las 9:12, Juan <juan@x.es> escribió:\n> ¿Quedamos?\n> Juan")]
    [InlineData("Vale, mañana a las 10.\n\nOn Mon, Nov 2, 2026 at 9:12 AM Juan <juan@x.es> wrote:\n> Shall we meet?")]
    [InlineData("Vale, mañana a las 10.\n\n-----Mensaje original-----\nDe: Juan\nEnviado: lunes\n¿Quedamos?")]
    [InlineData("Vale, mañana a las 10.\n\nDe: Juan García <juan@x.es>\nEnviado: lunes, 2 de noviembre de 2026 9:12\nPara: Ana\nAsunto: Quedamos")]
    [InlineData("Vale, mañana a las 10.\n--\nAna García\nDirectora")]
    [InlineData("Vale, mañana a las 10.\n\nEnviado desde mi iPhone")]
    public void QuotesAndSignaturesGo(string mail) => Assert.Equal("Vale, mañana a las 10.", MailText.WithoutQuotes(mail));

    [Fact]
    public void NeverEmpty()
    {
        Assert.Equal("> solo una cita", MailText.WithoutQuotes("> solo una cita"));
        Assert.Equal("Hola…", MailText.Snippet("Hola", 160).Replace("Hola", "Hola…", StringComparison.Ordinal));
        Assert.Equal(10, MailText.Snippet(new string('a', 50), 9).Length);
        Assert.Equal("factura_10_.pdf", MailText.SafeFileName("../..\\factura:10?.pdf"));
    }

    [Fact]
    public void ServersAndAutoconfig()
    {
        Assert.Equal("imap.gmail.com", MailAutoConfig.Lookup("ana@gmail.com")!.ImapHost);
        Assert.True(MailAutoConfig.Lookup("ana@hotmail.es")!.NeedsOAuth);
        Assert.Null(MailAutoConfig.Lookup("ana@midominio.es"));
        const string xml = """
            <clientConfig version="1.1"><emailProvider id="x.es">
              <incomingServer type="pop3"><hostname>pop.x.es</hostname><port>995</port><socketType>SSL</socketType></incomingServer>
              <incomingServer type="imap"><hostname>imap.x.es</hostname><port>143</port><socketType>plain</socketType></incomingServer>
              <incomingServer type="imap"><hostname>mail.x.es</hostname><port>993</port><socketType>SSL</socketType></incomingServer>
              <outgoingServer type="smtp"><hostname>smtp.x.es</hostname><port>587</port><socketType>STARTTLS</socketType></outgoingServer>
            </emailProvider></clientConfig>
            """;
        var servers = MailAutoConfig.Parse(xml)!;
        Assert.Equal(("mail.x.es", 993, "smtp.x.es", 587), (servers.ImapHost, servers.ImapPort, servers.SmtpHost, servers.SmtpPort));
    }

    [Fact]
    public void ImportantRules()
    {
        var rules = new MailRulesSettings { Senders = ["@iberdrola.es", "Hacienda"], Keywords = ["factura", "URGENTE"] };
        MailHeader Mail(string name, string address, string subject) => new() { From = new(name, address), Subject = subject };
        Assert.True(MailRules.IsImportant(Mail("Iberdrola", "clientes@iberdrola.es", "Novedades"), rules));
        Assert.True(MailRules.IsImportant(Mail("Agencia Tributaria · Hacienda", "no-reply@aeat.es", "Aviso"), rules));
        Assert.True(MailRules.IsImportant(Mail("Tienda", "x@tienda.com", "Tu Factura de noviembre"), rules));
        Assert.True(MailRules.IsImportant(Mail("Jefe", "jefe@x.com", "urgente: llamar"), rules));
        Assert.False(MailRules.IsImportant(Mail("Boletín", "news@x.com", "Ofertas"), rules));
        Assert.True(MailRules.Matches(Mail("Iberdrola", "clientes@iberdrola.es", "Factura"), "IBERDROLA factura"));
        Assert.Equal(("mail-1", "INBOX/Sub", 7u, 42u), MailHeader.ParseId(MailHeader.MakeId("mail-1", "INBOX/Sub", 7, 42)));
    }
}
