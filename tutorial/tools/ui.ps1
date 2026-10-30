param([int]$ProcessId, [string]$Name, [string]$Value, [switch]$List, [string]$Prefix, [switch]$Select, [switch]$Expand, [int]$Index = 0, [string]$Type, [int]$Up = 0)
# UI Automation over every visible top-level window of a process (no mouse). -List prints named elements; -Name acts on
# the element with that name (Index-th match; -Type limits the control type, e.g. ListItem): invoke, toggle, select
# (-Select) or expand (-Expand); -Value sets a text box.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$root = $A::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)
$windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
$walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
function Walk($e, $depth) {
  $c = $walker.GetFirstChild($e)
  while ($c -ne $null) { $c; if ($depth -lt 45) { Walk $c ($depth + 1) }; $c = $walker.GetNextSibling($c) }
}
$all = @(foreach ($w in $windows) { $w; Walk $w 0 })
if ($List) {
  foreach ($e in $all) { $n = $e.Current.Name; if ($n -and (-not $Prefix -or $n -like "*$Prefix*")) { "{0} | {1}" -f $e.Current.ControlType.ProgrammaticName.Replace('ControlType.', ''), $n } }
  exit 0
}
$named = @($all | Where-Object { ($_.Current.Name -eq $Name -or ($Name -eq '#edit' -and $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Edit')) -and (-not $Type -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$Type") })
if ($named.Count -le $Index) { "not found: $Name ($($named.Count))"; exit 1 }
$t = $named[$Index]
for ($i = 0; $i -lt $Up; $i++) { $t = $walker.GetParent($t) }
$p = $null
if ($PSBoundParameters.ContainsKey('Value')) {
  $t = $named | Where-Object { $_.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$p) } | Select-Object -First 1
  $t.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value); 'set'; exit 0
}
if ($Select -and $t.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); 'selected'; exit 0 }
if ($Expand -and $t.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) { $p.Expand(); 'expanded'; exit 0 }
if ($t.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); 'invoked'; exit 0 }
if ($t.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$p)) { $p.Toggle(); 'toggled'; exit 0 }
if ($t.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); 'selected'; exit 0 }
if ($t.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) { $p.Expand(); 'expanded'; exit 0 }
'no pattern'
