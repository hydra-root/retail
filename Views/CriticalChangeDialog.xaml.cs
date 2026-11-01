using System.Windows;
using System.Windows.Threading;

namespace RetailBillingApp.Views;

/// <summary>
/// SEC-05: modal prompt for the separate critical-change passcode (closed periods and
/// deletes). Same look and modal handling as the USR-06 <see cref="PasscodeDialog" />, but
/// it also names the action, explains why it is gated, takes an optional reason, and can
/// display a live lockout countdown.
///
/// The dialog is input-only: the caller (a ViewModel) feeds the entered values to
/// <c>ICriticalChangeGuard.VerifyAsync</c> and re-opens the prompt with the refusal message.
/// Verification stays outside on purpose — running an async service from a click handler on
/// the UI thread would mean blocking on <c>.Result</c>, which deadlocks WPF.
/// The <see cref="PasswordBox" /> is read in code-behind only (Password is not a dependency
/// property), so the entered secret never becomes a binding and never leaves this window
/// except through the returned <see cref="CriticalChangePrompt" />.
/// </summary>
public partial class CriticalChangeDialog : Window
{
    private DateTime _lockoutUntilUtc;
    private DispatcherTimer? _lockoutTimer;

    public CriticalChangeDialog()
    {
        InitializeComponent();
        Closed += (_, _) => _lockoutTimer?.Stop();
    }

    /// <summary>
    /// Shows the prompt and returns what was entered, or null when the operator cancelled
    /// (or the window was closed). Mirrors <c>PasscodeDialog.Show</c>.
    /// </summary>
    /// <param name="action">What is being changed, e.g. "Delete invoice INV-2026-0004".</param>
    /// <param name="gateDescription">Why the action is gated (SEC-05 explanation line).</param>
    /// <param name="inlineError">Previous refusal text, shown inline when set.</param>
    /// <param name="lockedUntilUtc">When given, the inputs stay frozen and a countdown runs until this instant.</param>
    /// <param name="attemptsRemaining">Shown as a hint so the operator knows the lockout budget.</param>
    public static CriticalChangePrompt? Show(string action, string gateDescription,
        string? inlineError = null, DateTime? lockedUntilUtc = null, int attemptsRemaining = 0) =>
        Show(null, action, gateDescription, inlineError, lockedUntilUtc, attemptsRemaining);

    public static CriticalChangePrompt? Show(Window? owner, string action, string gateDescription,
        string? inlineError = null, DateTime? lockedUntilUtc = null, int attemptsRemaining = 0)
    {
        var dialog = new CriticalChangeDialog
        {
            Owner = owner ?? System.Windows.Application.Current?.Windows
                .OfType<Window>().FirstOrDefault(w => w.IsActive)
        };

        dialog.ActionText.Text = action;
        dialog.GateText.Text = gateDescription;

        if (!string.IsNullOrWhiteSpace(inlineError))
            dialog.ShowError(inlineError!);

        if (attemptsRemaining > 0)
        {
            dialog.AttemptsText.Text = $"{attemptsRemaining} attempt(s) left before this gate locks.";
            dialog.AttemptsText.Visibility = Visibility.Visible;
        }

        // Countdown only while the window is genuinely still shut.
        if (lockedUntilUtc is { } until && until > DateTime.UtcNow)
            dialog.StartLockout(until);

        var confirmed = dialog.ShowDialog() == true;
        return confirmed
            ? new CriticalChangePrompt(dialog.PasscodeBox.Password, dialog.ReasonBox.Text)
            : null;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PasscodeBox.Password))
        {
            ShowError("Please enter the critical-change passcode.");
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        PasscodeBox.Clear();
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    // ---- lockout countdown ----

    private void StartLockout(DateTime untilUtc)
    {
        _lockoutUntilUtc = untilUtc;

        PasscodeBox.IsEnabled = false;
        ReasonBox.IsEnabled = false;
        OkButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        LockoutPanel.Visibility = Visibility.Visible;
        UpdateLockoutText();

        _lockoutTimer?.Stop();
        _lockoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lockoutTimer.Tick += (_, _) =>
        {
            if (DateTime.UtcNow >= _lockoutUntilUtc)
            {
                EndLockout();
                return;
            }
            UpdateLockoutText();
        };
        _lockoutTimer.Start();
    }

    private void UpdateLockoutText()
    {
        var remaining = _lockoutUntilUtc - DateTime.UtcNow;
        if (remaining.TotalSeconds < 0) remaining = TimeSpan.Zero;
        LockoutText.Text =
            $"Too many wrong passcodes — this gate is locked. " +
            $"Try again in {(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}.";
    }

    private void EndLockout()
    {
        _lockoutTimer?.Stop();
        _lockoutTimer = null;

        PasscodeBox.IsEnabled = true;
        ReasonBox.IsEnabled = true;
        OkButton.IsEnabled = true;
        LockoutText.Text = "The lockout has expired — the passcode prompt is open again.";
        PasscodeBox.Focus();
    }
}

/// <summary>
/// SEC-05: what the operator entered in <see cref="CriticalChangeDialog" />.
/// Short-lived by design — hand it straight to the guard and let it go.
/// </summary>
public sealed class CriticalChangePrompt
{
    public CriticalChangePrompt(string passcode, string? reason)
    {
        Passcode = passcode ?? string.Empty;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public string Passcode { get; }
    public string? Reason { get; }
}
