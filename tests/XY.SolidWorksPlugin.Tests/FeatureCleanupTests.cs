using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;
namespace XY.SolidWorksPlugin.Tests;
[CollectionDefinition("Feature cleanup UI", DisableParallelization = true)]
public sealed class FeatureCleanupUiCollection { }
[Collection("Feature cleanup UI")]
public sealed class FeatureCleanupTests
{
 static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);
 static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
 static void Sta(Action action) { Exception? error=null; var thread=new Thread(()=>{try{action();}catch(Exception ex){error=ex;}}); thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)throw error; }
 [Fact] public void RetiredWheelEntryIsRemovedButLegacyComboCannotSilentlySkipIt()
 {
  var config=ConfigStore.Deserialize("{}");ConfigStore.Normalize(config);
  var ring=config.Menus[SceneKeys.Part].Rings[0];
  ring.Items.Add(new MenuItemConfig { Slot=0, Kind=ActionKinds.Basic, Value="cut-list-bom" });
  ring.Items.Add(new MenuItemConfig { Slot=1, Kind=ActionKinds.Basic, Value="zoom-fit" });
  var combo=new MenuItemConfig { Slot=2, Kind=ActionKinds.Combo, Value="legacy", Label="legacy" };
  combo.ComboCommands.Add(new ComboCommandConfig { Kind=ActionKinds.Basic, Value="cut-list-bom" });
  combo.ComboCommands.Add(new ComboCommandConfig { Kind=ActionKinds.Basic, Value="zoom-fit" });ring.Items.Add(combo);
  ConfigStore.Normalize(config);
  Assert.False(BasicActions.Contains("cut-list-bom"));
  Assert.DoesNotContain(ring.Items, i=>i.Value=="cut-list-bom");Assert.Contains(ring.Items,i=>i.Value=="zoom-fit");
  Assert.Equal(2,combo.ComboCommands.Count);Assert.Equal("cut-list-bom",combo.ComboCommands[0].Value);
  Assert.Throws<InvalidOperationException>(()=>ProductFeatureScope.EnsureExecutable(combo));
 }
 [Fact] public void DisabledOptionalActionsAreRejectedWhileNormalActionsRemainAvailable()
 {
  var config=ConfigStore.Deserialize("{}");ConfigStore.Normalize(config);config.ProcessRecognition.Enabled=false;
  Assert.False(SwAddin.IsBasicActionEnabled(config,"process-recognition"));Assert.False(SwAddin.IsBasicActionEnabled(config,"panel-gap-adjustment"));Assert.True(SwAddin.IsBasicActionEnabled(config,"zoom-fit"));
  config.ProcessRecognition.Enabled=true;
  Assert.True(SwAddin.IsBasicActionEnabled(config,"process-recognition"));Assert.False(SwAddin.IsBasicActionEnabled(config,"panel-gap-adjustment"));
  config.ExplodeEnabled=false;
  Assert.False(SwAddin.IsBasicActionEnabled(config,"hole-color"));Assert.False(SwAddin.IsBasicActionEnabled(config,"hole-process"));
  Assert.False(SwAddin.IsBasicActionEnabled(config,"panel-gap-display"));Assert.False(SwAddin.IsBasicActionEnabled(config,"explode-view"));
  Assert.False(SwAddin.IsBasicActionEnabled(config,"explode-all"));Assert.False(SwAddin.IsBasicActionEnabled(config,"quote"));
 }

 [Fact] public void IndependentFeatureSwitchesLoadAndSaveSeparately() => Sta(() =>
 {
  var config=ConfigStore.Deserialize("{}");ConfigStore.Normalize(config);
  using var settings=new SettingsWindow(()=>ConfigStore.Clone(config),c=>config=ConfigStore.Clone(c));
  Call(settings,"RenderSection","features");
  Field<CheckBox>(settings,"_radialMenuEnabled").Checked=false;
  Field<CheckBox>(settings,"_shortcutPaletteEnabled").Checked=true;
  Field<CheckBox>(settings,"_propertyWatermarkEnabled").Checked=true;
  Field<CheckBox>(settings,"_processEnabled").Checked=false;
  Call(settings,"SaveConfig");
  Assert.False(config.RadialMenuEnabled);Assert.True(config.ShortcutPaletteEnabled);
  Assert.True(config.PropertyWatermark.Enabled);Assert.False(config.ProcessRecognition.Enabled);
  
  foreach(Form notice in settings.OwnedForms)notice.Close();
 });

 [Theory]
 [InlineData(false)]
 [InlineData(true)]
 public void OneClickExplosionSwitchLoadsAndPersistsIndependently(bool enabled) => Sta(() =>
 {
  var config=ConfigStore.Deserialize("{}");ConfigStore.Normalize(config);
  config.RadialMenuEnabled=false;config.ShortcutPaletteEnabled=false;
  config.PropertyWatermark.Enabled=false;config.ProcessRecognition.Enabled=false;config.ExplodeEnabled=enabled;
  using var settings=new SettingsWindow(()=>ConfigStore.Clone(config),c=>config=ConfigStore.Clone(c),mode:SettingsWindowMode.FeatureTools);
  var explosion=Field<CheckBox>(settings,"_explodeEnabled");
  Assert.Equal(enabled,explosion.Checked);Assert.True(explosion.Enabled);
  explosion.Checked=!enabled;
  Call(settings,"SaveConfig");
  Assert.Equal(!enabled,config.ExplodeEnabled);
  Assert.False(config.RadialMenuEnabled);Assert.False(config.ShortcutPaletteEnabled);
  Assert.False(config.PropertyWatermark.Enabled);Assert.False(config.ProcessRecognition.Enabled);
  Field<PluginConfig>(settings,"_config").ExplodeEnabled=enabled;
  Call(settings,"LoadConfigIntoControls");
  Assert.Equal(enabled,explosion.Checked);
  foreach(Form notice in settings.OwnedForms)notice.Close();
 });
 [Fact] public void FeatureDependenciesPreserveSelectionsAndSeparateEditorsPreserveEachOthersSettings() => Sta(()=>
 {
  var current=ConfigStore.Deserialize("{}");ConfigStore.Normalize(current);current.SketchWheelEnabled=true;current.GlobalComboAdvanceEnabled=true;
  using var settings=new SettingsWindow(()=>ConfigStore.Clone(current), c=>current=ConfigStore.Clone(c));
  Call(settings,"RenderSection","features");
  var radial=Field<CheckBox>(settings,"_radialMenuEnabled");var sketch=Field<System.Collections.Generic.Dictionary<string,CheckBox>>(settings,"_sceneSwitches")[SceneKeys.Sketch];var advance=Field<ComboBox>(settings,"_comboAdvanceKey");
  radial.Checked=false;Assert.False(sketch.Enabled);Assert.True(sketch.Checked);Assert.False(advance.Enabled);radial.Checked=true;Assert.True(sketch.Enabled);Assert.True(advance.Enabled);
  Field<CheckBox>(settings,"_processEnabled").Checked=false;Assert.False(Field<CheckBox>(settings,"_processAssemblyAuto").Enabled);
  for(int i=0;i<3;i++){Call(settings,"RenderSection","process");Call(settings,"RenderSection","basic");Call(settings,"RenderSection","features");}
  current.PropertyWatermark.DisplayScalePercent=145;
  Field<CheckBox>(settings,"_propertyWatermarkEnabled").Checked=false;Call(settings,"SaveConfig");
  Assert.Equal(145,current.PropertyWatermark.DisplayScalePercent);Assert.False(current.PropertyWatermark.Enabled);
  using var watermark=new PropertyWatermarkSettingsWindow(()=>ConfigStore.Clone(current),c=>current=ConfigStore.Clone(c),()=>null!,()=>{});
  current.RadialMenuEnabled=false;current.PropertyWatermark.Enabled=true;
  Field<NumericUpDown>(watermark,"_displayScale").Value=125;Call(watermark,"ApplyConfig");
  Assert.False(current.RadialMenuEnabled);Assert.True(current.PropertyWatermark.Enabled);Assert.Equal(125,current.PropertyWatermark.DisplayScalePercent);
  foreach(Form notice in settings.OwnedForms)notice.Close();
 });
}
