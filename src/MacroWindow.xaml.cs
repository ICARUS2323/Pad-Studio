using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace PadStudio;
public partial class MacroWindow : Window
{
    readonly List<int> selected=new();
    readonly Dictionary<int,Button> buttons=new();
    List<MacroStep> steps;
    public List<MacroStep> Result=>steps;
    public MacroWindow(List<MacroStep> existing)
    {
        InitializeComponent(); steps=JsonSerializer.Deserialize<List<MacroStep>>(JsonSerializer.Serialize(existing))!;
        foreach(var row in KeyboardCatalog.Rows) {
            var grid=new Grid { Margin=new Thickness(0,3,0,3),Height=38 };
            int column=0;
            foreach(var key in row) {
                grid.ColumnDefinitions.Add(new(){Width=new GridLength(key.Width,GridUnitType.Star)});
                var button=new Button { Content=key.Label,Padding=new Thickness(1),Margin=new Thickness(3,0,3,0),FontSize=11,Tag=key.Vk,ToolTip=key.Label };
                button.SetResourceReference(BackgroundProperty,"Key");
                button.Click+=(_,_)=> { if(!selected.Remove(key.Vk)) selected.Add(key.Vk); RefreshSelected(); };
                Grid.SetColumn(button,column++); grid.Children.Add(button); buttons[key.Vk]=button;
            }
            KeyboardPanel.Children.Add(grid);
        }
        RefreshSteps();
    }
    void RefreshSelected()
    {
        foreach(var pair in buttons) { bool on=selected.Contains(pair.Key); pair.Value.SetResourceReference(BackgroundProperty,on?"KeySelected":"Key"); pair.Value.SetResourceReference(BorderBrushProperty,on?"Accent":"GlassEdge"); }
        SelectedKeysText.Text=selected.Count==0?"Klavyeden tuş seç…":string.Join(" + ",selected.Select(KeyboardCatalog.Name));
    }
    void RefreshSteps(int index=-1) { StepsList.ItemsSource=null; StepsList.ItemsSource=steps; StepsList.SelectedIndex=index; CountText.Text=$"{steps.Count} adım"; }
    void AddKeys_Click(object sender,RoutedEventArgs e)
    {
        if(selected.Count==0) { MessageBox.Show(this,"Önce klavyeden tuş seç."); return; }
        if(ModeBox.SelectedIndex==0) steps.Add(new(){Kind=StepKind.Chord,Keys=selected.ToList()});
        else foreach(var key in selected) steps.Add(new(){Kind=StepKind.Chord,Keys=new(){key}});
        RefreshSteps(); selected.Clear(); RefreshSelected(); StepsList.ScrollIntoView(steps.Last());
    }
    void AddDelay_Click(object sender,RoutedEventArgs e)
    {
        if(!int.TryParse(DelayBox.Text,out var ms) || ms is <0 or >60000) { MessageBox.Show(this,"0–60000 arasında bir milisaniye değeri yaz."); return; }
        steps.Add(new(){Kind=StepKind.Delay,DelayMs=ms}); RefreshSteps(steps.Count-1);
    }
    void AddText_Click(object sender,RoutedEventArgs e)
    {
        if(string.IsNullOrEmpty(StepTextBox.Text)) { StepTextBox.Focus(); return; }
        steps.Add(new(){Kind=StepKind.Text,Text=StepTextBox.Text}); StepTextBox.Clear(); RefreshSteps(steps.Count-1);
    }
    void Move(int offset)
    {
        int i=StepsList.SelectedIndex,j=i+offset; if(i<0 || j<0 || j>=steps.Count) return;
        (steps[i],steps[j])=(steps[j],steps[i]); RefreshSteps(j);
    }
    void MoveUp_Click(object sender,RoutedEventArgs e)=>Move(-1);
    void MoveDown_Click(object sender,RoutedEventArgs e)=>Move(1);
    void Remove_Click(object sender,RoutedEventArgs e) { int i=StepsList.SelectedIndex; if(i<0) return; steps.RemoveAt(i); RefreshSteps(Math.Min(i,steps.Count-1)); }
    void Step_Selected(object sender,SelectionChangedEventArgs e)
    {
        if(StepsList.SelectedItem is not MacroStep step) return;
        if(step.Kind==StepKind.Chord) { selected.Clear(); selected.AddRange(step.Keys); RefreshSelected(); }
        else if(step.Kind==StepKind.Delay) DelayBox.Text=step.DelayMs.ToString();
        else StepTextBox.Text=step.Text;
    }
    void UpdateStep_Click(object sender,RoutedEventArgs e)
    {
        int i=StepsList.SelectedIndex; if(i<0) return; var step=steps[i];
        if(step.Kind==StepKind.Chord) { if(selected.Count==0) return; step.Keys=selected.ToList(); }
        else if(step.Kind==StepKind.Delay) { if(!int.TryParse(DelayBox.Text,out var ms)||ms is <0 or >60000) return; step.DelayMs=ms; }
        else step.Text=StepTextBox.Text;
        RefreshSteps(i);
    }
    void Apply_Click(object sender,RoutedEventArgs e)
    {
        try { ActionValidator.Validate(new(){Title="Makro",Kind=ActionKind.Macro,Steps=steps}); DialogResult=true; } catch(Exception ex) { MessageBox.Show(this,ex.Message,"Makroyu kontrol et"); }
    }
    void Cancel_Click(object sender,RoutedEventArgs e)=>DialogResult=false;
    internal void PreviewSelection(params int[] keys) { selected.Clear(); selected.AddRange(keys); RefreshSelected(); }
}
