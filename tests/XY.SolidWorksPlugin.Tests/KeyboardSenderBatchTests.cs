using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

// Every test supplies a fake sender and key-state reader. None calls the real
// SendInput/GetAsyncKeyState or sends a key to the desktop/host.
public sealed class KeyboardSenderBatchTests
{
 [Fact]
 public void Win32InputUnionHasNativeArchitectureAlignment()
 {
  Assert.Equal(IntPtr.Size==8?40:28,KeyboardSender.InputSize);
  Assert.Equal(IntPtr.Size==8?8:4,Marshal.OffsetOf(typeof(KeyboardSender.Input),"Data").ToInt32());
  Assert.Equal(0,Marshal.OffsetOf(typeof(KeyboardSender.InputUnion),"Keyboard").ToInt32());
  Assert.Equal(0,Marshal.OffsetOf(typeof(KeyboardSender.InputUnion),"Mouse").ToInt32());
  Assert.Equal(0,Marshal.OffsetOf(typeof(KeyboardSender.InputUnion),"Hardware").ToInt32());
  Assert.Equal(IntPtr.Size==8?24:16,Marshal.SizeOf(typeof(KeyboardSender.KeyboardInput)));
  Assert.Equal(IntPtr.Size==8?32:24,Marshal.SizeOf(typeof(KeyboardSender.InputUnion)));
  Assert.Equal(IntPtr.Size==8?16:12,Marshal.OffsetOf(typeof(KeyboardSender.KeyboardInput),"ExtraInfo").ToInt32());
 }
 [Fact]
 public void CompleteShortcutIsOneOrderedNativeBatch()
 {
  var probe=new Probe();
  KeyboardSender.Send("Ctrl+Shift+Alt+Win+K",probe.Send,probe.Pressed);
  Assert.Single(probe.Calls);
  Assert.Equal("11D,10D,12D,5BD,4BD,4BU,5BU,12U,10U,11U",Events(probe.Calls[0]));
  Assert.All(probe.Calls[0],input=>Assert.Equal(1u,input.Type));
  Assert.Equal(KeyboardSender.InputSize,Assert.Single(probe.Sizes));
 }
 [Fact]
 public void AlreadyHeldRequestedModifiersAreNeverInjectedOrReleased()
 {
  var probe=new Probe(0x11,0x10,0x12,0x5C);
  KeyboardSender.Send("Ctrl+Shift+Alt+Win+K",probe.Send,probe.Pressed);
  Assert.Equal("4BD,4BU",Events(Assert.Single(probe.Calls)));
  Assert.Equal(new[]{0x11,0x10,0x12,0x5B,0x5C},probe.Queries);
 }
 [Theory]
 [InlineData(0,"")]
 [InlineData(1,"11U")]
 [InlineData(2,"41U,11U")]
 [InlineData(3,"11U")]
 public void PartialBatchReleasesOnlyConfirmedOwnedDownsAndNeverReplays(uint inserted,string cleanup)
 {
  var probe=new Probe {FirstCount=inserted};
  var error=Assert.Throws<InvalidOperationException>(()=>KeyboardSender.Send("Ctrl+A",probe.Send,probe.Pressed));
  Assert.Contains(inserted+"/4",error.Message);
  Assert.Equal("11D,41D,41U,11U",Events(probe.Calls[0]));
  Assert.Equal(cleanup.Length==0?1:2,probe.Calls.Count);
  if(cleanup.Length>0)
  {
   Assert.Equal(cleanup,Events(probe.Calls[1]));
   Assert.All(probe.Calls[1],input=>Assert.True((input.Data.Keyboard.Flags&2)!=0));
  }
  Assert.Equal(new[]{0x11},probe.Queries);
 }
 [Theory]
 [InlineData(2,"10U,11U")]
 [InlineData(3,"41U,10U,11U")]
 [InlineData(4,"10U,11U")]
 [InlineData(5,"11U")]
 public void MultiModifierPartialCleanupFollowsReverseAcquisitionOrder(uint inserted,string expected)
 {
  var probe=new Probe {FirstCount=inserted};
  Assert.Throws<InvalidOperationException>(()=>KeyboardSender.Send("Ctrl+Shift+A",probe.Send,probe.Pressed));
  Assert.Equal(expected,Events(probe.Calls[1]));Assert.Equal(2,probe.Calls.Count);
 }
 [Fact]
 public void PhysicalControlIsNotReleasedEvenWhenPrimaryDownWasPartiallySent()
 {
  var probe=new Probe(0x11) {FirstCount=1};
  Assert.Throws<InvalidOperationException>(()=>KeyboardSender.Send("Ctrl+A",probe.Send,probe.Pressed));
  Assert.Equal("41D,41U",Events(probe.Calls[0]));Assert.Equal("41U",Events(probe.Calls[1]));
  Assert.DoesNotContain(probe.Calls.SelectMany(call=>call),input=>input.Data.Keyboard.VirtualKey==0x11);
 }
 [Fact]
 public void CleanupDoesNotMistakeInjectedDownForAUsersPhysicalHold()
 {
  var calls=new List<KeyboardSender.Input[]>();int stateReads=0;
  Assert.Throws<InvalidOperationException>(()=>KeyboardSender.Send("Ctrl+A",(events,size)=>
  {
   calls.Add(events.ToArray());return calls.Count==1?1u:(uint)events.Length;
  },key=>
  {
   stateReads++;Assert.Empty(calls);return false;
  }));
  Assert.Equal(1,stateReads);Assert.Equal("11U",Events(calls[1]));Assert.Equal(2,calls.Count);
 }
 [Fact]
 public void UnconfirmedSenderExceptionDoesNotReplayOrGuessAReleasePrefix()
 {
  int calls=0;
  var error=Assert.Throws<InvalidOperationException>(()=>KeyboardSender.Send("Ctrl+A",(events,size)=>
  {
   calls++;throw new InvalidOperationException("sender unavailable");
  },key=>false));
  Assert.Equal(1,calls);Assert.NotNull(error.InnerException);Assert.Contains("未重新发送",error.Message);
 }
 [Fact]
 public void FailedReleaseCountIsReportedWithoutAnotherShortcutAttempt()
 {
  var probe=new Probe {FirstCount=2,CleanupCount=0};
  var error=Assert.Throws<InvalidOperationException>(()=>KeyboardSender.Send("Ctrl+A",probe.Send,probe.Pressed));
  Assert.Contains("释放清理未完成（0/2）",error.Message);Assert.Equal(2,probe.Calls.Count);
  Assert.Equal("41U,11U",Events(probe.Calls[1]));
 }
 [Fact]
 public void ReleaseExceptionIsReportedWithoutReplayingAnyDown()
 {
  int calls=0;
  var error=Assert.Throws<InvalidOperationException>(()=>KeyboardSender.Send("Ctrl+A",(events,size)=>
  {
   if(++calls==1)return 1;
   Assert.Equal("11U",Events(events));throw new InvalidOperationException("release blocked");
  },key=>false));
  Assert.Equal(2,calls);Assert.Contains("释放清理失败",error.Message);
 }
 [Fact]
 public void ImpossibleNativeCountCannotTriggerAReplay()
 {
  var probe=new Probe {FirstCount=5};
  Assert.Throws<InvalidOperationException>(()=>KeyboardSender.Send("Ctrl+A",probe.Send,probe.Pressed));
  Assert.Single(probe.Calls);
 }
 [Fact]
 public void RepeatedModifierAliasesAreSentOnceEach()
 {
  var probe=new Probe();KeyboardSender.Send("Ctrl+Control+Shift+SHIFT+A",probe.Send,probe.Pressed);
  Assert.Equal("11D,10D,41D,41U,10U,11U",Events(Assert.Single(probe.Calls)));
  Assert.Equal(new[]{0x11,0x10},probe.Queries);
 }
 [Fact]
 public void KeyboardLayoutImpliedModifiersRemainInTheSameOrder()
 {
  var probe=new Probe();int scans=0;
  KeyboardSender.Send("Alt+@",probe.Send,probe.Pressed,ch=>
  {
   scans++;Assert.Equal('@',ch);return (short)(0x0300|'2');
  });
  Assert.Equal(1,scans);Assert.Equal("12D,10D,11D,32D,32U,11U,10U,12U",Events(Assert.Single(probe.Calls)));
 }
 [Theory]
 [InlineData("Control+Return",0x0D)]
 [InlineData("Alt+Page Down",0x22)]
 [InlineData("Shift+Num Divide",0x6F)]
 [InlineData("Win+Delete",0x2E)]
 [InlineData("Ctrl+Forward Slash",0xBF)]
 [InlineData("Ctrl+Semicolon",0xBA)]
 [InlineData("Ctrl+NumPlus",0x6B)]
 [InlineData("Ctrl+Left Brace",0xDB)]
 [InlineData("Ctrl+F24",0x87)]
 [InlineData("Ctrl+Num 6",0x66)]
 [InlineData("Ctrl+Num *",0x6A)]
 [InlineData("Ctrl+Num -",0x6D)]
 [InlineData("Ctrl+Quotation Mark",0xDE)]
 public void ExistingSolidWorksShortcutNamesKeepTheirVirtualKeys(string hotkey,int key)
 {
  var probe=new Probe();KeyboardSender.Send(hotkey,probe.Send,probe.Pressed,ch=>throw new InvalidOperationException("Named tokens do not scan a layout."));
  var inputs=Assert.Single(probe.Calls);
  Assert.Equal(2,inputs.Count(input=>input.Data.Keyboard.VirtualKey==key));
  Assert.Equal((ushort)key,inputs.Single(input=>input.Data.Keyboard.VirtualKey==key&&(input.Data.Keyboard.Flags&2)==0).Data.Keyboard.VirtualKey);
 }
 [Theory]
 [InlineData("PageUp",0x21)]
 [InlineData("Home",0x24)]
 [InlineData("Delete",0x2E)]
 [InlineData("Right",0x27)]
 [InlineData("NumDivide",0x6F)]
 public void ExtendedNavigationKeysCarryTheirNativeExtendedFlagOnDownAndUp(string hotkey,int key)
 {
  var probe=new Probe();KeyboardSender.Send(hotkey,probe.Send,probe.Pressed);
  var inputs=Assert.Single(probe.Calls);Assert.Equal(2,inputs.Length);
  Assert.All(inputs,input=>{Assert.Equal((ushort)key,input.Data.Keyboard.VirtualKey);Assert.Equal(1u,input.Data.Keyboard.Flags&1);});
 }
 [Theory]
 [InlineData("")]
 [InlineData("Ctrl+Shift")]
 [InlineData("Ctrl+UnsupportedKeyName")]
 public void InvalidHotkeysFailBeforeReadingStateOrSendingAnything(string hotkey)
 {
  var probe=new Probe();Assert.Throws<InvalidOperationException>(()=>KeyboardSender.Send(hotkey,probe.Send,probe.Pressed));
  Assert.Empty(probe.Calls);Assert.Empty(probe.Queries);
 }
 private static string Events(IEnumerable<KeyboardSender.Input> events)=>string.Join(",",events.Select(input=>input.Data.Keyboard.VirtualKey.ToString("X2")+((input.Data.Keyboard.Flags&2)!=0?"U":"D")));
 private sealed class Probe
 {
  internal readonly List<KeyboardSender.Input[]> Calls=new List<KeyboardSender.Input[]>();
  internal readonly List<int> Sizes=new List<int>(),Queries=new List<int>();
  internal uint FirstCount=uint.MaxValue,CleanupCount=uint.MaxValue;
  private readonly HashSet<int> _held;
  internal Probe(params int[] held){_held=new HashSet<int>(held);}
  internal bool Pressed(int key)
  {
   Assert.Empty(Calls);Queries.Add(key);return _held.Contains(key);
  }
  internal uint Send(KeyboardSender.Input[] events,int size)
  {
   Calls.Add(events.ToArray());Sizes.Add(size);
   uint count=Calls.Count==1?FirstCount:CleanupCount;
   return count==uint.MaxValue?(uint)events.Length:count;
  }
 }
}

