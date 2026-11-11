using System.Windows;
using System.Windows.Controls;
namespace SongSense.App;
public sealed class ConfirmationWindow : Window
{
 public ConfirmationWindow(string title, string message, string accept, string cancel = "Cancelar", string? context = null)
 {
  Title=title;Width=480;Height=480;MinWidth=360;MinHeight=320;
  MaxHeight=SystemParameters.WorkArea.Height;SetResourceReference(BackgroundProperty,"Canvas");SetResourceReference(ForegroundProperty,"Ink");WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new DockPanel{Margin=new Thickness(24)};
  var actions=new ActionPanel{Gap=12,Margin=new Thickness(0,16,0,0)};
  var safe=new Button{Content=cancel,IsDefault=true,IsCancel=true};var confirm=new Button{Content=accept};confirm.SetResourceReference(StyleProperty,"DangerButton");
  safe.Click+=(_,_)=>DialogResult=false;confirm.Click+=(_,_)=>DialogResult=true;actions.Children.Add(safe);actions.Children.Add(confirm);DockPanel.SetDock(actions,Dock.Bottom);panel.Children.Add(actions);
  var body=new StackPanel();var heading=new TextBlock{Text=title,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)};heading.SetResourceReference(TextBlock.FontSizeProperty,"UiHeading20");body.Children.Add(heading);
  if(!string.IsNullOrWhiteSpace(context))body.Children.Add(new TextBox{Text=context,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
  body.Children.Add(new TextBox{Text=message,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Background=System.Windows.Media.Brushes.Transparent});
  panel.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Content=panel;
  SizeChanged+=(_,_)=>{UiLayout.SetIsCompact(this,ActualWidth<600);panel.Margin=new Thickness(ActualWidth<600?12:24);};
  Loaded+=(_,_)=>safe.Focus();SourceInitialized+=(_,_)=>WindowAppearance.Apply(this);
 }
 public static bool Ask(Window owner,string title,string message,string accept,string cancel="Cancelar",string? context=null)=>new ConfirmationWindow(title,message,accept,cancel,context){Owner=owner}.ShowDialog()==true;
}
public sealed record CacheDeletionRequest(bool All, long? TrackRevision, long LyricsRevision, long Epoch, string Label);
