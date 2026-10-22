using KDesktop.Assistant.Brain.Talk;

namespace KDesktop.Assistant.Tests;

/// <summary>5.0.131: what the model says it did, against what it did.</summary>
public class ActionClaimsTests
{
    [Fact]
    public void ASendThatNeverHappenedIsTakenOutAndTheRestStays()
    {
        // 22/10: it opened the Telegram app and t.me, and said it had sent the list.
        var (text, unfounded) = ActionClaims.Check(
            "Te he enviado a Telegram los 42 usuarios conectados al clúster 3. En el calendario solo hay una cita el miércoles 28/10 a las 13:00.",
            ["mcp_ft42_conectados", "run_command", "mcp_calendar_list_events", "open_web"]);
        Assert.Equal(["send"], unfounded);
        Assert.Equal("En el calendario solo hay una cita el miércoles 28/10 a las 13:00. No lo he enviado: no tenía cómo hacerlo.", text);
    }

    [Theory]
    [InlineData("Te lo he enviado al Telegram.", new[] { "send_telegram" })]
    [InlineData("Te lo he mandado.", new[] { "mcp_email_send_message" })]
    [InlineData("Lo he apuntado en tus notas.", new[] { "save_note" })]
    [InlineData("He abierto Steam.", new[] { "run_command" })]
    [InlineData("Te avisaré cuando termine la descarga.", new[] { "notify_when" })]
    [InlineData("No he enviado nada todavía.", new string[0])]
    [InlineData("Hay 43 personas conectadas en el cluster 3.", new string[0])]
    public void WhatWasDoneOrIsNotAClaimStays(string said, string[] worked)
    {
        var (text, unfounded) = ActionClaims.Check(said, worked);
        Assert.Empty(unfounded);
        Assert.Equal(said, text);
    }

    [Theory]
    [InlineData("I've sent it to your phone.", "send")]
    [InlineData("Lo he guardado en tu memoria.", "save")]
    [InlineData("He borrado la nota.", "delete")]
    [InlineData("He cerrado Chrome.", "close")]
    [InlineData("He programado la alarma para las 7.", "schedule")]
    public void AClaimWithNothingOfItsKindDoneGoes(string said, string kind)
    {
        var (text, unfounded) = ActionClaims.Check(said, ["get_time"]);
        Assert.Equal([kind], unfounded);
        Assert.DoesNotContain(said, text, StringComparison.Ordinal);
    }
}
