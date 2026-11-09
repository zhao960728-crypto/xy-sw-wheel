using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal static class ThirdPartyIconResources
{
	internal static string LastReadDetails = "";
	internal static string AssetName(string label)
	{
		string name = RadialCommandIcons.NameKey(label);
		switch (name)
		{
			case "删特征": return "删除特征";
			case "极速删特征": return "删除特征极速版";
			case "改实体厚度": return "修改实体厚度";
			case "改名三合一": return "批量改名三合一";
			case "设为正视图": return "快速设置为正视图";
			case "压铆拉铆牙孔": return "压铆拉铆";
			default: return name;
		}
	}
	// Some add-ins publish a named icon archive (e.g. 博士图标/002_钣金BOM.png).
	// Read only image assets from already loaded assemblies, without loading an
	// add-in, invoking its callbacks, or changing its toolbar/activation settings.
	internal static Dictionary<string, Bitmap> ReadLoaded(ISldWorks host)
	{
		var result = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
		var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var archives = new HashSet<string>(StringComparer.Ordinal);
		var details = new System.Text.StringBuilder();
		var assemblies = new List<System.Reflection.Assembly>();
		// COM add-ins can live in another CLR application domain. Ask only for
		// already connected objects; their public resource assembly stays readable.
		var seenAddins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
		foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
		try
		{
			using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
			using RegistryKey addins = baseKey.OpenSubKey(@"SOFTWARE\SolidWorks\Addins");
			foreach (string id in addins?.GetSubKeyNames() ?? Array.Empty<string>())
			{
				if (seenAddins.Contains(id)) continue;
				using var key = addins.OpenSubKey(id);
				string title = Convert.ToString(key?.GetValue("Title")) ?? "";
				if (title.IndexOf("Verification",StringComparison.OrdinalIgnoreCase)>=0 || title.IndexOf("Bridge",StringComparison.OrdinalIgnoreCase)>=0) continue;
				try { var addin = host?.GetAddInObject(id); if (addin != null) { assemblies.Insert(0, addin.GetType().Assembly); seenAddins.Add(id); } }
				catch (Exception ex) { Logger.Trace("Connected icon provider unavailable: "+ex.Message); }
			}
		}
		catch (Exception ex) { Logger.Trace("Read SolidWorks add-in icon registry skipped: " + hive + "/" + view + ": " + ex.Message); }
		foreach (var assembly in assemblies.GroupBy(a=>a.FullName).Select(g=>g.First()))
		{
			try
			{
				foreach (string resource in assembly.GetManifestResourceNames().Where(n => IsImage(n)).Take(512))
				{
					using var content=assembly.GetManifestResourceStream(resource);
					if(content==null || content.Length>1024*1024)continue;
					string fileName=Path.GetFileNameWithoutExtension(resource);
					string name=RadialCommandIcons.NameKey(fileName.Substring(fileName.LastIndexOf('.')+1));
					AddImage(result,ambiguous,name,content);
				}
				foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith("Icons.zip", StringComparison.OrdinalIgnoreCase)))
				{
					using var stream = assembly.GetManifestResourceStream(resource);
					if (stream == null || stream.Length > 16*1024*1024) continue;
					// An add-in may expose the same archive through multiple assemblies.
					// Only different content can introduce a genuine name conflict.
					using (var hash = SHA256.Create())
						if (!archives.Add(Convert.ToBase64String(hash.ComputeHash(stream)))) continue;
					stream.Position = 0;
					using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
					details.Append(resource).Append(':').Append(archive.Entries.Count).Append(';');
					foreach (var entry in archive.Entries.Take(512))
					{
						if (result.Count >= 512 || entry.Length > 1024*1024 || !IsImage(entry.Name)) continue;
						string name = RadialCommandIcons.NameKey(Regex.Replace(Path.GetFileNameWithoutExtension(entry.Name), @"^\d+_", ""));
						using var content = entry.Open();
						AddImage(result,ambiguous,name,content);
					}
				}
			}
			catch (Exception ex) { if(details.Length<2000) details.Append(ex.Message).Append(';'); Logger.Trace("Third-party icon resources skipped: " + ex.Message); }
		}
		LastReadDetails = "icons="+result.Count+";"+details;
		return result;
	}

	private static bool IsImage(string name) => new[]{".png",".bmp",".jpg",".jpeg"}.Contains(Path.GetExtension(name).ToLowerInvariant());
	internal static void AddImage(Dictionary<string,Bitmap> result,HashSet<string> ambiguous,string name,Stream content)
	{
		if(name.Length==0 || name.Contains("未使用") || ambiguous.Contains(name) || result.Count>=512)return;
		try
		{
			using var buffer=new MemoryStream();var bytes=new byte[8192];int read;
			while((read=content.Read(bytes,0,bytes.Length))>0){if(buffer.Length+read>1024*1024)return;buffer.Write(bytes,0,read);}
			buffer.Position=0;using var image=Image.FromStream(buffer);
			if(image.Width>1024 || image.Height>1024)return;
			if(result.TryGetValue(name,out var previous))
			{
				using var priorThumbnail=new Bitmap(previous,32,32);
				using var thumbnail=new Bitmap(image,32,32);
				bool same=true;
				for(int y=0;y<32 && same;y++)for(int x=0;x<32;x++)if(priorThumbnail.GetPixel(x,y)!=thumbnail.GetPixel(x,y)){same=false;break;}
				if(!same){previous.Dispose();result.Remove(name);ambiguous.Add(name);}
				else if(image.Width*image.Height>previous.Width*previous.Height){result[name]=WheelIconDrawing.CopySource(image);previous.Dispose();}
				return;
			}
			result[name]=WheelIconDrawing.CopySource(image);
		}
		catch(Exception ex){Logger.Trace("Named third-party icon skipped: "+ex.Message);}
	}
}
