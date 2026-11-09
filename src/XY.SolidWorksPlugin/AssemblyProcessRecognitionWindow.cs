using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Diagnostics;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace XY.SolidWorksPlugin;

internal sealed class AssemblyProcessRecognitionWindow : PluginToolWindow
{
	private const int CatalogSliceSize = 8;
	private readonly Func<ISldWorks> _getSolidWorks;
	private readonly IModelDoc2 _assemblyDocument;
	private ProcessRecognitionConfig _config;
	private bool _contextPaused;
	internal IModelDoc2 OwnedDocument => _assemblyDocument;
	internal void PauseForContext() { _contextPaused = true; Hide(); }
	internal void RefreshSettings(ProcessRecognitionConfig config)
	{
		if (MatchesSettings(config)) return;
		_cache.Save(); _config = ProcessRulesWindow.Copy(config);
		_cache = new ProcessRecognitionBatchCache(_config);
		foreach (var part in _parts) if (part.Edited || part.PendingProperties.Count > 0) part.Revalidate = true;
	}
	private readonly Action _onCompleted;
	private ProcessRecognitionBatchCache _cache;
	private bool _automaticBackground;
	private bool _readAutomaticDeep;
	private bool? _effectiveReadDeep;
	private bool EffectiveDeep => _effectiveReadDeep ?? (_deepMode || _readAutomaticDeep);
	private bool _deepWorkloadConfirmed;
	private string _workloadReadNotice = "";
	internal Func<ProcessRecognitionWorkload, string, bool> RecognitionConfirmationForTest { get; set; }
	private bool _catalogIsPartial;
	private bool _fullCatalogPending;
	private readonly Label _summary = new Label { AutoSize = true, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) };
	private readonly Label _status = new Label { AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105) };
	private readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Fill, Height = 20 };
	private readonly DataGridView _grid = new DataGridView
	{
		Dock = DockStyle.Fill,
		AllowUserToAddRows = false,
		AllowUserToDeleteRows = false,
		AllowUserToResizeRows = false,
		ReadOnly = false,
		EditMode = DataGridViewEditMode.EditProgrammatically,
		RowHeadersVisible = false,
		AutoGenerateColumns = false,
		SelectionMode = DataGridViewSelectionMode.FullRowSelect,
		MultiSelect = true
	};
	private readonly Button _pause = new Button { Text = "暂停", AutoSize = true, Height = 31 };
	private readonly Button _cancel = new Button { Text = "取消任务", AutoSize = true, Height = 31 };
	private readonly ComboBox _batchProcess = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Key" };
	private readonly ComboBox _category = new ComboBox { Width = 145, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Key" };
	internal Action<ProcessRecognitionConfig> SaveRules { get; set; }
	private readonly TextBox _attributeRemark = new TextBox { Width = 180 };
	private readonly Queue<ManualWrite> _manualWrites = new Queue<ManualWrite>();
	private readonly HashSet<BatchPart> _queuedParts = new HashSet<BatchPart>();
	private readonly HashSet<BatchPart> _editingParts = new HashSet<BatchPart>();
	private readonly TextBox _filter = new TextBox { Width = 190 };
	private readonly ComboBox _filterState = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
	private bool _paused;
	private bool _cancelled;
	private bool _running;
	private bool _processingSlice;
	private bool _queuePrepared;
	private bool _catalogPreparing;
	private List<BatchPart> _parts = new List<BatchPart>();
	private Dictionary<string, BatchPart> _uniqueParts = new Dictionary<string, BatchPart>(StringComparer.OrdinalIgnoreCase);
	private object[] _componentItems = Array.Empty<object>();
	private int _componentScanIndex;
	private int _instanceCount;
	private int _nextIndex;
	private int _unavailableCount;
	private DateTime _lastSliceUtc = DateTime.MinValue;
	private bool _deepMode;
	private readonly string _assemblyKey;
	private readonly string _configuration;
	private bool _shutdown;
    private bool _sidebarPrepared, _buildingStepEditor;
    internal Action SidebarCloseRequested { get; set; }
    private readonly CheckBox _independentSteps = new CheckBox { Text = "逐道工序下拉编辑", AutoSize = true };
    private readonly FlowLayoutPanel _stepEditor = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Visible = false };
	private int _resultStamp;
	private string _settingsKey;

	public void ResumeForDisplay()
	{
		_contextPaused = false;
		if (_parts.Any(part => part.Edited || part.PendingProperties.Count > 0 || part.HoleGeometryPending || part.PendingHoleFacts.Length > 0) || _manualWrites.Count > 0)
		{
			_status.Text = "已恢复工序草稿和待写入内容；写入前会再次检查模型是否变化。";
			return;
		}
		// Restoring a hidden results window must not COM-poll every referenced
		// component. Keep its validated display snapshot while the assembly and
		// rules are unchanged; each target is still revalidated immediately before
		// a write, and the user can explicitly request a fresh recognition pass.
		bool referencedFileChanged = _parts.Any(part => !string.IsNullOrWhiteSpace(part.Path)
			&& part.SnapshotFileStamp.Length > 0 && part.SnapshotFileStamp != FileStamp(part.Path));
		if (_resultStamp != _assemblyDocument.GetUpdateStamp() || _settingsKey != ProcessRecognitionBatchCache.SettingsKey(_config) || referencedFileChanged)
		{
			_running = false;
			_queuePrepared = false;
			_catalogPreparing = false;
			_cancelled = false;
			_manualWrites.Clear();
			PrepareQueue();
		}
		else if (!_running && _queuePrepared) _status.Text = "已恢复上次识别结果；引用文件时间戳未变。写入前仍会逐项核验模型。";
	}

	private static bool PartUnchanged(BatchPart part)
	{
		try
		{
			string configuration = string.IsNullOrWhiteSpace(part.Configuration)
				? part.Document.ConfigurationManager.ActiveConfiguration.Name : part.Configuration;
			return part.Document.GetUpdateStamp() == part.Stamp && part.Document.GetConfigurationByName(configuration) != null;
		}
		catch { return false; }
	}

	public void PromoteToInteractive()
	{
		_automaticBackground = false;
		if (!_catalogIsPartial) return;
		_fullCatalogPending = true;
		if (!_catalogPreparing && !_running) ExpandInteractiveCatalog();
	}

	public void CloseForShutdown() { _shutdown = true; Close(); }

	internal void ApplyHoleProcessFacts(ProcessHoleFactMessage message)
	{
		if (IsDisposed || message == null || message.Source == "ProcessRead") return;
		foreach (var part in _parts.Where(p => Equals(p.Document, message.Document) && string.Equals(p.Configuration, message.Configuration, StringComparison.OrdinalIgnoreCase)))
		{
			if (part.AnalysisInput == null || part.Row == null || _queuedParts.Contains(part)) continue;
			part.AnalysisInput.HoleFacts = message.Facts;
			if (part.TypeGroupInput != null) part.TypeGroupInput.HoleFacts = message.Facts;
			if (part.Stamp != message.UpdateStamp)
			{
				part.HoleGeometryPending = true;
				ProcessHoleSuggestionPolicy.MarkGeometryPending(part.AnalysisInput.Geometry);
				ProcessHoleSuggestionPolicy.MarkGeometryPending(part.TypeGroupInput?.Geometry);
			}
			if (!part.Edited && part.ExistingProcessEdit == null && !part.HoleGeometryPending && !_editingParts.Contains(part))
				part.Row.Cells["Process"].Value = MergeHoleSummary(part, message.Facts);
			else part.PendingHoleFacts = message.Facts;
			part.Row.Cells["State"].Value = part.HoleGeometryPending ? "孔工艺已更新，几何待复核；原工序保留，可合并建议后重新轻量识别" : "孔工艺建议已更新；已有工序或人工草稿可明确合并";
		}
	}
	private string MergeHoleSummary(BatchPart part, IEnumerable<ProcessHoleFact> facts)
	{
		var rows = NormalizeManualProcess(part.Row.Cells["Process"].Value?.ToString() ?? "").Split(new[] { _config.Separator }, StringSplitOptions.RemoveEmptyEntries).Select(name => new RecognizedProcess { Name = name.Trim(), Evidence = "现有工序草稿" });
		return ProcessRecognitionService.Join(ProcessHoleSuggestionPolicy.MergeMissing(rows, facts, _config).Select(p => p.Name), _config.Separator);
	}
	private void AcceptSelectedHoleSuggestions()
	{
		if (!_grid.EndEdit()) return;
		int count = 0;
		foreach (var row in SelectedVisiblePartRows())
		{
			if (!(row.Tag is BatchPart part) || part.PendingHoleFacts.Length == 0 || _queuedParts.Contains(part)) continue;
			string before = row.Cells["Process"].Value?.ToString() ?? "";
			string merged = MergeHoleSummary(part, part.PendingHoleFacts); part.PendingHoleFacts = Array.Empty<ProcessHoleFact>();
			if (before == merged) continue;
			row.Cells["Process"].Value = merged; part.Edited = part.ProcessEditedManually = part.HoleOnlyIncrement = true; count++;
			row.Cells["State"].Value = part.HoleGeometryPending ? "已合并确认孔工序；请重新轻量识别核对模型后写入，草稿保持" : "确认孔工序已合并到草稿，待明确写入；类型属性保持";
		}
		_status.Text = count > 0 ? "已合并 " + count + " 行确认孔工序建议，模型尚未写入。" : "所选行没有新的确认孔工序建议。";
		RefreshStepEditor();
	}

	public AssemblyProcessRecognitionWindow(Func<ISldWorks> getSolidWorks, IModelDoc2 assemblyDocument, ProcessRecognitionConfig config, Action onCompleted, bool automaticBackground = false)
	{
		_getSolidWorks = getSolidWorks ?? throw new ArgumentNullException(nameof(getSolidWorks));
		_assemblyDocument = assemblyDocument ?? throw new ArgumentNullException(nameof(assemblyDocument));
		_config = config ?? new ProcessRecognitionConfig();
		ProcessRecognitionConfig.Normalize(_config);
		_cache = new ProcessRecognitionBatchCache(_config);
		_onCompleted = onCompleted;
		_automaticBackground = automaticBackground;
		_readAutomaticDeep = automaticBackground && _config.DefaultMode == ProcessRecognitionModes.Deep;
		_assemblyKey = DocumentKey(_assemblyDocument);
		_configuration = _assemblyDocument.ConfigurationManager.ActiveConfiguration.Name;
		_resultStamp = _assemblyDocument.GetUpdateStamp();
		_settingsKey = ProcessRecognitionBatchCache.SettingsKey(_config);
		Text = "装配体工序识别预览";
		Size = new Size(1010, 700);
		MinimumSize = new Size(1010, 700);
		StartPosition = FormStartPosition.CenterScreen;
		Font = new Font("Microsoft YaHei UI", 9f);
		BuildGrid();
		Controls.Add(BuildLayout());
		_pause.Click += (_, __) => TogglePause();
		_cancel.Click += (_, __) => CancelPending();
		_grid.CellEndEdit += OnProcessEdited;
		_grid.CellBeginEdit += (_, e) => { if (_grid.Rows[e.RowIndex].Tag is BatchPart part && _queuedParts.Contains(part)) e.Cancel = true; };
		FormClosing += OnFormClosing;
		Shown += (_, __) => StartBackground();
	}

	public bool IsAutomaticBackground => _automaticBackground;
	public bool MatchesSettings(ProcessRecognitionConfig config) => _settingsKey == ProcessRecognitionBatchCache.SettingsKey(config);

	public void StartBackground()
	{
		if (_queuePrepared || _running) return;
		_status.Text = Visible
			? "正在读取装配体清单；列表先显示，工序识别仍在后台分片中运行。"
			: "自动轻量识别已排队；等待宿主可安全调用时建立去重队列，不参与装配体打开和加载。";
		if (Visible && IsHandleCreated)
		{
			BeginInvoke((Action)PrepareVisibleQueue);
		}
	}

	private void PrepareVisibleQueue()
	{
		if (IsDisposed || !Visible || _queuePrepared || _running || _cancelled) return;
		PrepareQueue();
	}

	public bool MatchesDocument(IModelDoc2 document)
	{
		try { return Equals(document, _assemblyDocument) && document != null && _configuration == document.ConfigurationManager.ActiveConfiguration.Name; }
		catch { return false; }
	}

	private Control BuildLayout()
	{
		TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 8, ColumnCount = 1 };
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		_summary.Text = "当前装配体：" + _assemblyDocument.GetTitle();
		layout.Controls.Add(_summary, 0, 0);
		_status.Text = "正在整理装配体零件；快速识别会在每个小批次之间让出界面控制。";
		layout.Controls.Add(_status, 0, 1);
		layout.Controls.Add(new Label
		{
			AutoSize = true,
			Text = "操作顺序：先选表格中的零件行，再选工序组并套用；双击“工序明细”可逐道编辑、拖动换序、插入或删除，最后点“写入所选行”。编辑工序组内容请点“编辑工序组…”。",
			ForeColor = Color.FromArgb(71, 85, 105),
			Margin = new Padding(0, 4, 0, 6)
		}, 0, 2);
		layout.Controls.Add(_progress, 0, 3);
		FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 8, 0, 8) };
		Button close = new Button { Text = "关闭", AutoSize = true, Height = 31 };
		close.Click += (_, __) => Close();
		Button deep = new Button { Text = "当前及下属深度识别", AutoSize = true, Height = 31 };
		deep.Click += (_, __) => Restart(deep: true);
		Button quick = new Button { Text = "重新轻量识别", AutoSize = true, Height = 31 };
		quick.Click += (_, __) => Restart(deep: false);
		Button batchWrite = new Button
		{
		Text = "写入所选行",
			AutoSize = true,
			Height = 31,
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(30, 136, 229),
			ForeColor = Color.White
		};
		batchWrite.FlatAppearance.BorderColor = batchWrite.BackColor;
		batchWrite.Click += (_, __) => QueueSelectedWrites();
		_batchProcess.Items.Add(new KeyValuePair<string, string>("按各行工序", ""));
		_batchProcess.Items.AddRange(ProcessRecognitionConfig.ParseCombinations(_config.ProcessCombinations).Cast<object>().ToArray());
		_batchProcess.SelectedIndex = 0;
		actions.Controls.Add(new Label { Text = "工序组", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
		actions.Controls.Add(_batchProcess);
		actions.Controls.Add(AttributeButton("套用工序组到所选行", StageSelectedProcessGroup));
		actions.Controls.Add(AttributeButton("编辑工序组…", EditRules));
		actions.Controls.Add(AttributeButton("工序查看/分析", ShowAnalysis));
		actions.Controls.Add(AttributeButton("合并所选孔工序建议", AcceptSelectedHoleSuggestions));
		actions.Controls.Add(AttributeButton("按类型套组到所选行", StageSelectedTypeGroups));
		actions.Controls.Add(AttributeButton("编辑所选零件工序…", EditSelectedPartSequence));
		actions.Controls.Add(AttributeButton("标记所选为外购", () => StageSelectedPurchaseMarkers(true)));
		actions.Controls.Add(AttributeButton("取消所选外购（设否）", () => StageSelectedPurchaseMarkers(false)));
		actions.Controls.Add(quick);
		actions.Controls.Add(deep);
		actions.Controls.Add(_pause);
		actions.Controls.Add(_cancel);
		actions.Controls.Add(close);
		FlowLayoutPanel attributes = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 10, 0, 0) };
		attributes.Controls.Add(new Label { Text = "待写入属性（选中零件）", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 8, 0) });
		_category.Items.AddRange(ProcessRecognitionConfig.ParseProcessNameMappings(_config.ProcessCategories).Select(pair => (object)pair).ToArray());
		if (_category.Items.Count > 0) _category.SelectedIndex = 0;
		attributes.Controls.Add(_category);
		attributes.Controls.Add(AttributeButton("应用类别", () => { if (_category.SelectedItem is KeyValuePair<string, string> pair) StageSelectedPropertyWrites(_config.ProcessTypePropertyName, pair.Value); }));
		attributes.Controls.Add(new Label { Text = "备注值", AutoSize = true, Margin = new Padding(8, 8, 4, 0) });
		attributes.Controls.Add(_attributeRemark);
		_filterState.Items.AddRange(new object[] { "全部状态", "待确认", "失败 / 跳过", "已写入" }); _filterState.SelectedIndex = 0;
		attributes.Controls.Add(new Label { Text = "筛选", AutoSize = true, Margin = new Padding(8, 8, 4, 0) });
		attributes.Controls.Add(_filter); attributes.Controls.Add(_filterState);
		_filter.TextChanged += (_, __) => FilterRows(); _filterState.SelectedIndexChanged += (_, __) => FilterRows();
		attributes.Controls.Add(AttributeButton("备注待写入", () => StageSelectedPropertyWrites(_config.RemarkPropertyName, _attributeRemark.Text)));
		layout.Controls.Add(actions, 0, 4);
		layout.Controls.Add(_grid, 0, 5);
		layout.Controls.Add(attributes, 0, 6);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var writes = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Margin = new Padding(0, 8, 0, 0) };
        writes.Controls.Add(batchWrite);
        writes.Controls.Add(AttributeButton("写入所有可写行", QueueAllWrites));
        writes.Controls.Add(AttributeButton("确认所选外购标记", QueueSelectedPurchaseMarkers));
        layout.Controls.Add(writes, 0, 7);
		return layout;
	}

	private static Button AttributeButton(string text, Action action)
	{
		Button button = new Button { Text = text, AutoSize = true, Height = 31 };
		button.Click += (_, __) => action();
		return button;
	}

	private void BuildGrid()
    {
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Checked", HeaderText = "选", Width = 42 });
        _grid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Checked") _grid.Rows[e.RowIndex].Cells["Checked"].Value = !Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells["Checked"].Value ?? false); };
        _grid.CurrentCellChanged += (_, __) => RefreshStepEditor();
        _independentSteps.CheckedChanged += (_, __) => RefreshStepEditor();
		_grid.CellDoubleClick += OnPartCellDoubleClick;
        _grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.F2 && _grid.CurrentCell?.OwningColumn.Name == "Process") { e.Handled = true; EditSelectedPartSequence(); } };
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Order", HeaderText = "序号", Width = 55, ReadOnly = true });
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Part", HeaderText = "零件", Width = 220, ReadOnly = true });
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Configuration", HeaderText = "引用配置", Width = 150, ReadOnly = true });
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "判定", Width = 115, ReadOnly = true });
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Process", HeaderText = "工序明细（可直接修改）", Width = 350 });
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "状态", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Purchase", HeaderText = "外购身份 / 待确认标记", ReadOnly = true, Width = 230 });
	}

	private void PrepareQueue()
	{
		if (_running || _queuePrepared || _catalogPreparing) return;
		_queuePrepared = true;
		try
		{
			if (!(_assemblyDocument is IAssemblyDoc assembly)) throw new InvalidOperationException("当前文档不是装配体。");
			_status.Text = "正在分片读取装配体清单；发现一个先显示一个，不执行几何识别。";
			_status.Update();
			object value = assembly.GetComponents(false);
			IEnumerable<object> components = value is Array componentArray ? componentArray.Cast<object>() : Enumerable.Empty<object>();
			object[] allComponents = components.ToArray();
			_catalogIsPartial = false;
			_componentItems = allComponents;
			_componentScanIndex = 0;
			_instanceCount = 0;
			_unavailableCount = 0;
			_parts = new List<BatchPart>();
			_uniqueParts = new Dictionary<string, BatchPart>(StringComparer.OrdinalIgnoreCase);
			_grid.Rows.Clear();
			_progress.Maximum = Math.Max(1, _componentItems.Length);
			_progress.Value = 0;
			_catalogPreparing = true;
			if (_deepMode || PurchasedPartPolicy.IsPurchased(PurchasedPartPolicy.ReadReferencedIdentity(_assemblyDocument, _configuration, _assemblyDocument.GetTitle(), _config))) AddRootAssemblyPart();
			ProcessCatalogSlice();
		}
		catch (Exception ex)
		{
			_queuePrepared = false;
			_catalogPreparing = false;
			_cancelled = true;
			Logger.Error("Assembly batch process recognition failed", ex);
			_status.Text = "批量识别停止：" + ex.Message;
		}
	}

	public void OnSolidWorksIdle()
	{
		if (_processingSlice || _paused || _contextPaused || IsDisposed) return;
		if (_cancelled) return;
		if (!IsAssemblyContextActive()) return;
		if (_catalogPreparing)
		{
			if (_cancelled) return;
			_processingSlice = true;
			try { ProcessCatalogSlice(); }
			catch (Exception ex)
			{
				_catalogPreparing = false;
				_cancelled = true;
				_running = false;
				_status.Text = "清单读取已停止：" + ex.Message + "；请重新识别。";
				Logger.Error("Assembly catalog stopped", ex);
			}
			finally { _processingSlice = false; }
			return;
		}
		if (_manualWrites.Count > 0)
		{
			if (!IsAssemblyContextActive()) return;
            // Keep a row stable while its editor is open; other confirmed rows can proceed.
            int pending = _manualWrites.Count;
            while (pending-- > 0 && _editingParts.Contains(_manualWrites.Peek().Part)) _manualWrites.Enqueue(_manualWrites.Dequeue());
            if (_editingParts.Contains(_manualWrites.Peek().Part)) return;
			_processingSlice = true;
			try
			{
				ManualWrite write = _manualWrites.Dequeue();
				try { ProcessManualWrite(write); }
				finally { _queuedParts.Remove(write.Part); }
			}
			finally { _processingSlice = false; }
			return;
		}
		if (!_queuePrepared)
		{
			if (_cancelled) return;
			PrepareQueue();
			return;
		}
		if (!_running) return;
		if (_cancelled)
		{
			FinishQueue(cancelled: true);
			return;
		}
		if (_nextIndex >= _parts.Count)
		{
			FinishQueue(cancelled: false);
			return;
		}
		if ((DateTime.UtcNow - _lastSliceUtc).TotalMilliseconds < _config.AssemblyBatchDelayMs) return;
		BatchPart next = _parts[_nextIndex];
		if (!next.IsCached && !IsAssemblyContextActive())
		{
			_status.Text = "当前已离开该装配体，后台识别自动暂停；返回装配体后继续。";
			return;
		}
		if (_editingParts.Contains(next)) return;
		_processingSlice = true;
		try
		{
			int sliceSize = next.IsCached ? 20 : 1;
            var sliceClock = Stopwatch.StartNew();
			int end = Math.Min(_parts.Count, _nextIndex + sliceSize);
			while (_nextIndex < end && !_cancelled)
			{
				ProcessPart(_parts[_nextIndex], _nextIndex, _parts.Count);
				_nextIndex++;
				_progress.Value = Math.Min(_parts.Count, _nextIndex);
				if (sliceClock.ElapsedMilliseconds >= 20 || (_nextIndex < _parts.Count && _editingParts.Contains(_parts[_nextIndex]))) break;
			}
			_lastSliceUtc = DateTime.UtcNow;
		}
		finally
		{
			_processingSlice = false;
		}
	}

	private void FinishQueue(bool cancelled)
	{
		if (!_running) return;
		_running = false;
		_cache.Save();
		if (!cancelled && _fullCatalogPending)
		{
			ExpandInteractiveCatalog();
			return;
		}
		_resultStamp = _assemblyDocument.GetUpdateStamp();
		_settingsKey = ProcessRecognitionBatchCache.SettingsKey(_config);
		_pause.Enabled = false;
		_cancel.Enabled = false;
		int failed = _parts.Count(part => part.Row?.Cells["Type"].Value?.ToString() == "失败");
		int skipped = _parts.Count(part => new[] { "未加载", "多配置", "待深度" }.Contains(part.Row?.Cells["Type"].Value?.ToString()));
		_status.Text = cancelled
			? "批量识别已取消；已完成 " + _nextIndex + "/" + _parts.Count + " 个文件/配置行。"
			: (_deepMode ? "深度" : "轻量") + "识别结束：成功 " + (_parts.Count - failed - skipped) + "，失败 " + failed + "，跳过 " + skipped + "；待人工确认写入。" + (_unavailableCount > 0 ? " 另跳过 " + _unavailableCount + " 个抑制/缺失引用组件。" : "");
		FilterRows();
		_onCompleted?.Invoke();
	}

	private bool IsAssemblyContextActive()
	{
		try
		{
			IModelDoc2 active = _getSolidWorks()?.ActiveDoc as IModelDoc2;
			return active != null && active.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY && MatchesDocument(active);
		}
		catch { return false; }
	}

	private void ProcessCatalogSlice()
	{
		if (!_catalogPreparing) return;
		int end = Math.Min(_componentItems.Length, _componentScanIndex + CatalogSliceSize);
        var sliceClock = Stopwatch.StartNew();
		_grid.SuspendLayout();
		try
		{
			while (_componentScanIndex < end && !_cancelled)
			{
				ProcessCatalogComponent(_componentItems[_componentScanIndex]);
				_componentScanIndex++;
                if (sliceClock.ElapsedMilliseconds >= 20) break;
			}
		}
		finally { _grid.ResumeLayout(); }
		_progress.Value = Math.Min(_progress.Maximum, _componentScanIndex);
		_summary.Text = "当前装配体：" + _assemblyDocument.GetTitle() + "；清单 " + _componentScanIndex + "/" + _componentItems.Length + "，文件/配置行 " + _parts.Count + " 个";
		if (_cancelled)
		{
			_catalogPreparing = false;
			_status.Text = "清单读取已取消；已显示 " + _parts.Count + " 个文件/配置行。";
			return;
		}
		if (_componentScanIndex < _componentItems.Length)
		{
			_status.Text = "清单分片读取中：已先显示 " + _parts.Count + " 个文件/配置行；不会一次遍历全部组件。";
			return;
		}
		CompleteQueuePreparation();
	}

	private void ProcessCatalogComponent(object item)
	{
		if (!(item is IComponent2 component)) return;
		_instanceCount++;
		// Keep the purchased subassembly itself in the list, with its own
		// referenced-configuration marker; its internals are supplied as a unit.
		if (PurchasedPartPolicy.HasPurchasedAncestor(component, _config)) return;
		try
		{
			if (component.IsSuppressed())
			{
				_unavailableCount++;
				return;
			}
		}
		catch { }
		string path = "";
		try { path = component.GetPathName() ?? ""; }
		catch { }
		string referencedConfiguration = component.ReferencedConfiguration ?? "";
		bool supportedPath = path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase);
		IModelDoc2 document = null;
		{
			try { document = component.GetModelDoc2() as IModelDoc2; }
			catch { }
		}
		int loadedType = document?.GetType() ?? 0;
		bool supportedDocument = document == null || loadedType == (int)swDocumentTypes_e.swDocPART || loadedType == (int)swDocumentTypes_e.swDocASSEMBLY;
		if (!supportedDocument || (document == null && !supportedPath))
		{
			_unavailableCount++;
			return;
		}
		if (referencedConfiguration.Length == 0 && document != null)
		{
			try { referencedConfiguration = document.ConfigurationManager.ActiveConfiguration.Name ?? ""; }
			catch { }
		}
		string title = path.Length > 0 ? System.IO.Path.GetFileName(path) : (document?.GetTitle() ?? component.Name2 ?? "未命名零件");
		string key = PartConfigurationKey(path.Length > 0 ? path : title, referencedConfiguration);
		if (_uniqueParts.ContainsKey(key)) return;
		BatchPart part = new BatchPart { Document = document, Path = path, Name = title, Configuration = referencedConfiguration, ComponentName = component.Name2 ?? "" };
		part.SnapshotFileStamp = FileStamp(path);
		part.ReferencedConfigurations.Add(referencedConfiguration);
		if (document != null) part.Stamp = document.GetUpdateStamp();
		bool safeToUseCache = !_deepMode && (document == null || (!document.GetSaveFlag() && !document.IsOpenedReadOnly()));
		if (safeToUseCache && _cache.TryGet(path, referencedConfiguration, out string cachedType, out string cachedProcess))
		{
			part.CachedClassification = cachedType;
			part.CachedProcess = cachedProcess;
			if (_cache.TryGetSnapshotInfo(path, referencedConfiguration, false, out var cachedInput, out _, out bool cachedDeep)) {
				part.TypeGroupInput = cachedInput; part.AnalysisInput = cachedInput;
				part.Deep = cachedDeep;
				if (document != null) part.Stamp = document.GetUpdateStamp();
			} else if (EffectiveDeep) { part.CachedClassification = part.CachedProcess = ""; }
		}
		_uniqueParts[key] = part;
		_parts.Add(part);
		AppendGridRow(part);
	}

	private void AddRootAssemblyPart()
	{
		string rootPath = _assemblyDocument.GetPathName() ?? "";
		string rootConfiguration = _assemblyDocument.ConfigurationManager.ActiveConfiguration.Name ?? "";
		string rootKey = PartConfigurationKey(rootPath.Length > 0 ? rootPath : _assemblyDocument.GetTitle(), rootConfiguration);
		if (_uniqueParts.ContainsKey(rootKey)) return;
		BatchPart part = new BatchPart { Document = _assemblyDocument, Path = rootPath, Name = _assemblyDocument.GetTitle() ?? "当前装配体", IsRoot = true, Configuration = rootConfiguration };
		_uniqueParts[rootKey] = part;
		_parts.Add(part);
		AppendGridRow(part);
	}

	private void AppendGridRow(BatchPart part)
	{
		string type = part.IsCached ? part.CachedClassification : "识别中";
		string process = part.IsCached ? part.CachedProcess : "识别中";
		string state = part.IsCached ? "文件未变化，等待缓存确认" : "等待后台识别";
		int rowIndex = _grid.Rows.Add(false, _grid.Rows.Count + 1, part.Name, string.IsNullOrWhiteSpace(part.Configuration) ? "默认配置" : part.Configuration, type, process, state);
		DataGridViewRow row = _grid.Rows[rowIndex];
		row.Tag = part;
		part.Row = row;
	}

	private void CompleteQueuePreparation()
	{
		_catalogPreparing = false;
		// Do not retain thousands of component RCWs after cataloging. Each part is
		// resolved from its path only when its own idle slice starts.
		_componentItems = Array.Empty<object>();
		_progress.Maximum = Math.Max(1, _parts.Count);
		_progress.Value = 0;
		_summary.Text = "当前装配体：" + _assemblyDocument.GetTitle() + "；实例 " + _instanceCount + " 个，文件/配置行 " + _parts.Count + " 个";
		if (_parts.Count == 0)
		{
			if (_fullCatalogPending)
			{
				ExpandInteractiveCatalog();
				return;
			}
			_status.Text = _unavailableCount > 0 ? "装配体中只有抑制或缺失引用的组件，未修改任何文件。" : "装配体中没有可识别的零件。";
			return;
		}
		_running = true;
		_status.Text = "列表已显示；后台逐个快速识别，每个短片后让出界面；正常操作时继续推进。";
	}

	private void ProcessPart(BatchPart part, int index, int total)
	{
		DataGridViewRow row = part.Row;
		object previousType = row?.Cells["Type"].Value, previousProcess = row?.Cells["Process"].Value;
		if (((part.Edited || part.HoleGeometryPending || part.PendingHoleFacts.Length > 0) && !part.Revalidate) || _queuedParts.Contains(part)) return;
		bool preserveManual = part.Edited;
		string manualProcess = row?.Cells["Process"].Value?.ToString() ?? "";
		if (part.ReferencedConfigurations.Count > 1)
		{
			row.Cells["Type"].Value = "多配置"; row.Cells["Process"].Value = "";
			row.Cells["State"].Value = "该行合并了多个引用配置，请重新读取配置清单后分别识别。"; return;
		}
		if (row == null || row.Index < 0)
		{
			int rowIndex = _grid.Rows.Add(false, index + 1, part.Name, part.Configuration, "待识别", "", "等待后台识别");
			row = _grid.Rows[rowIndex];
			row.Tag = part;
			part.Row = row;
		}
		row.Cells["Type"].Value = "识别中";
		row.Cells["Process"].Value = preserveManual ? manualProcess : "识别中";
		row.Cells["State"].Value = "读取实体几何";
		_status.Text = "后台快速识别 " + (index + 1) + "/" + total + "：" + part.Name;
		if (part.IsCached && part.AnalysisInput != null && !part.Revalidate
			&& part.SnapshotFileStamp == FileStamp(part.Path)
			&& (part.Document == null || (!part.Document.GetSaveFlag() && PartUnchanged(part)))
			&& (part.Document != null || string.IsNullOrWhiteSpace(part.AnalysisInput.ExistingProcess)))
		{
			if (part.Document != null) part.ExistingProcessEdit = ExistingProcessEditTarget.Effective(part.Document, part.Configuration, _config.SummaryPropertyName);
			row.Cells["Type"].Value = part.CachedClassification;
			row.Cells["Process"].Value = part.ExistingProcessEdit == null ? part.CachedProcess
				: ProcessRecognitionService.Join(part.ExistingProcessEdit.Rows(_config.Separator).Select(p => p.Name), _config.Separator);
			row.Cells["Purchase"].Value = PurchasedPartPolicy.Display(part.AnalysisInput);
			row.Cells["State"].Value = part.Deep ? "文件未变化，缓存命中" : "文件未变化，复用轻量结果；深度几何仍需明确确认";
			return;
		}
		if (!_deepMode && part.Document == null)
		{
			part.Document = ResolveLoadedDocument(part);
			if (part.Document == null)
			{
				row.Cells["Type"].Value = "未加载";
				row.Cells["Process"].Value = "";
				row.Cells["State"].Value = "后台最小量模式已跳过，未打开文件";
				return;
			}
		}
		IModelDoc2 document = null;
		bool openedForBatch = false;
		try
		{
			document = ResolveDocument(part, out openedForBatch);
			if (document == null) throw new InvalidOperationException("零件无法在后台打开");
			bool wasDirty = document.GetSaveFlag();
			ProcessRecognitionInput input = ReadPartForReferencedConfiguration(part, document, openedForBatch, wasDirty);
			part.TypeGroupInput = input;
			part.AnalysisInput = input;
			part.Deep = EffectiveDeep;
			part.HoleGeometryPending = false;
			if (!preserveManual) part.ExistingProcessEdit = ExistingProcessEditTarget.Effective(document, part.Configuration, _config.SummaryPropertyName);
			row.Cells["Purchase"].Value = PurchasedPartPolicy.Display(input);
			IReadOnlyList<RecognizedProcess> processes = part.ExistingProcessEdit != null && !preserveManual ? part.ExistingProcessEdit.Rows(_config.Separator) : ProcessRecognitionService.Recognize(input, deep: EffectiveDeep, _config);
			string summary = ProcessRecognitionService.Join(processes.Select(process => process.Name), _config.Separator);
			if (summary.Length == 0 && preserveManual) summary = manualProcess;
			if (summary.Length == 0 && part.ExistingProcessEdit == null && !_deepMode)
			{
				part.Stamp = document.GetUpdateStamp(); part.Configuration = document.ConfigurationManager.ActiveConfiguration.Name;
				row.Cells["Type"].Value = "待深度"; row.Cells["Process"].Value = "";
				row.Cells["State"].Value = "轻量模式未找到工序依据，导入件请用深度识别或手动填写。"; return;
			}
			if (summary.Length == 0 && part.ExistingProcessEdit == null) throw new InvalidOperationException("没有可确认的工序，请手动填写");
			string state = part.ExistingProcessEdit == null ? "识别完成，待人工确定写入" : "模型已有工序（" + part.ExistingProcessEdit.ScopeLabel + "），可直接双击修改后确认";
			if (_workloadReadNotice.Length > 0) state += "；" + _workloadReadNotice;
			string classification = preserveManual ? "人工确认" : Classification(input, processes);
			part.Stamp = document.GetUpdateStamp();
			part.Configuration = !string.IsNullOrWhiteSpace(part.Configuration) ? part.Configuration : document.ConfigurationManager.ActiveConfiguration.Name;
			part.SnapshotFileStamp = FileStamp(part.Path);
			if (!preserveManual && !wasDirty && !_deepMode && !document.IsOpenedReadOnly() && !string.IsNullOrWhiteSpace(part.Path))
			{
				_cache.Put(part.Path, part.Configuration, classification, summary);
				part.CachedClassification = classification;
				part.CachedProcess = summary;
			}
			if (!preserveManual && !wasDirty && !document.IsOpenedReadOnly() && !string.IsNullOrWhiteSpace(part.Path))
				_cache.PutSnapshot(part.Path, part.Configuration, classification, input, processes, EffectiveDeep);
			if (openedForBatch && !wasDirty)
			{
				try { _getSolidWorks()?.CloseDoc(document.GetTitle()); }
				catch { }
				part.Document = null;
			}
			row.Cells["Type"].Value = classification;
			row.Cells["Process"].Value = preserveManual ? manualProcess : summary;
			row.Cells["State"].Value = preserveManual ? "模型已重新读取；手工工序保留，待确定写入" : state;
            part.Revalidate = false;
            if (_grid.CurrentRow == row) RefreshStepEditor();
		}
		catch (RecognitionDeclinedException)
		{
			row.Cells["Type"].Value = previousType; row.Cells["Process"].Value = previousProcess; row.Cells["State"].Value = "已取消本次深度识别，原结果和人工草稿保留";
			_cancelled = true; _running = false; _status.Text = "深度识别已停止，原列表结果保留。";
			if (openedForBatch && document != null && !document.GetSaveFlag()) { _getSolidWorks()?.CloseDoc(document.GetTitle()); part.Document = null; }
		}
		catch (Exception ex)
		{
			if (openedForBatch && document != null)
			{
				try
				{
					if (!document.GetSaveFlag()) _getSolidWorks()?.CloseDoc(document.GetTitle());
					else document.Visible = true;
				}
				catch { }
			}
			row.Cells["Type"].Value = "失败";
			row.Cells["State"].Value = ex.Message;
			Logger.Trace("Assembly batch process recognition skipped " + part.Name + ": " + ex.Message);
		}
	}

	private IModelDoc2 ResolveDocument(BatchPart part, out bool openedForBatch)
	{
		openedForBatch = false;
		if (part.Document != null)
		{
			try { if (part.Document.GetType() > 0) return part.Document; } catch { }
			part.Document = null;
		}
		if (string.IsNullOrWhiteSpace(part.Path)) return null;
		ISldWorks sw = _getSolidWorks();
		if (sw == null) return null;
		try
		{
			IModelDoc2 alreadyOpen = sw.GetOpenDocumentByName(part.Path) as IModelDoc2;
			if (alreadyOpen != null)
			{
				part.Document = alreadyOpen;
				return alreadyOpen;
			}
		}
		catch { }
		bool wasVisible = true;
		int documentType = part.Path.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase)
			? (int)swDocumentTypes_e.swDocASSEMBLY
			: (int)swDocumentTypes_e.swDocPART;
		try { wasVisible = sw.GetDocumentVisible(documentType); }
		catch { }
		try
		{
			sw.DocumentVisible(false, documentType);
			IDocumentSpecification specification = sw.GetOpenDocSpec(part.Path) as IDocumentSpecification;
			if (specification == null) return null;
			specification.Silent = true;
			specification.ReadOnly = false;
			specification.ViewOnly = false;
			specification.LoadModel = true;
			specification.ConfigurationName = part.Configuration ?? "";
			specification.AddToRecentDocumentList = 0;
			IModelDoc2 opened = sw.OpenDoc7(specification) as IModelDoc2;
			if (opened == null) return null;
			openedForBatch = true;
			part.Document = opened;
			if (!string.IsNullOrWhiteSpace(part.Configuration)
				&& !string.Equals(opened.ConfigurationManager?.ActiveConfiguration?.Name ?? "", part.Configuration, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("SOLIDWORKS 未能以引用配置“" + part.Configuration + "”打开模型。");
			return opened;
		}
		finally
		{
			try { sw.DocumentVisible(wasVisible, documentType); }
			catch { }
		}
	}

	private IModelDoc2 ResolveLoadedDocument(BatchPart part)
	{
		if (part.Document != null)
		{
			try { if (part.Document.GetType() > 0) return part.Document; } catch { }
			part.Document = null;
		}
		if (string.IsNullOrWhiteSpace(part.Path)) return null;
		try { return _getSolidWorks()?.GetOpenDocumentByName(part.Path) as IModelDoc2; }
		catch { return null; }
	}

	internal static string Classification(ProcessRecognitionInput input, IReadOnlyList<RecognizedProcess> processes)
	{
		return ProcessPartClassification.Display(input, processes);
	}

	private void Restart(bool deep)
	{
		_grid.EndEdit();
		if (_manualWrites.Count > 0) { _status.Text = "还有已确认的写入任务，请先完成或取消任务再重新识别。"; return; }
		if (deep)
		{
			var workload = ProcessRecognitionWorkload.Read(_assemblyDocument);
			if (!workload.Confirm(this, "当前及下属装配零件深度识别", RecognitionConfirmationForTest)) { _status.Text = "已取消深度识别，原列表和人工草稿保留。"; return; }
			_deepWorkloadConfirmed = workload.RequiresConfirmation;
		}
		else _deepWorkloadConfirmed = false;
		_effectiveReadDeep = null;
		bool continueCatalog = _catalogPreparing;
		_readAutomaticDeep = false;
		_cancelled = false;
		_paused = false;
		_running = false;
		_catalogPreparing = false;
		_deepMode = deep;
		_nextIndex = 0;
		_manualWrites.Clear();
		if (deep && !_parts.Any(part => part.IsRoot))
		{
			string rootPath = _assemblyDocument.GetPathName() ?? "";
			string rootConfiguration = _assemblyDocument.ConfigurationManager.ActiveConfiguration.Name ?? "";
			BatchPart root = new BatchPart { Document = _assemblyDocument, Path = rootPath, Name = _assemblyDocument.GetTitle() ?? "当前装配体", IsRoot = true, Configuration = rootConfiguration };
			_parts.Insert(0, root);
			_grid.Rows.Insert(0, false, 1, root.Name, string.IsNullOrWhiteSpace(root.Configuration) ? "默认配置" : root.Configuration, "识别中", "", "等待后台深度识别");
			root.Row = _grid.Rows[0];
			root.Row.Tag = root;
		}
		else if (!deep && !PurchasedPartPolicy.IsPurchased(PurchasedPartPolicy.ReadReferencedIdentity(_assemblyDocument, _configuration, _assemblyDocument.GetTitle(), _config)))
		{
			foreach (BatchPart root in _parts.Where(part => part.IsRoot).ToArray())
			{
				if (root.Row != null && root.Row.Index >= 0) _grid.Rows.Remove(root.Row);
				_parts.Remove(root);
			}
		}
		foreach (DataGridViewRow row in _grid.Rows)
		{
			row.Cells["Order"].Value = row.Index + 1;
			if (row.Tag is BatchPart part)
			{
				if (part.Edited || part.PendingProperties.Count > 0 || part.HoleGeometryPending || part.PendingHoleFacts.Length > 0) { part.Revalidate = true; part.CachedClassification = part.CachedProcess = ""; row.Cells["State"].Value = "工序草稿和孔建议已保留，等待重新读取模型"; continue; }
				part.CachedClassification = "";
				part.CachedProcess = "";
			}
			row.Cells["Order"].Value = row.Index + 1;
			row.Cells["State"].Value = deep ? "等待后台深度识别" : "等待后台轻量识别";
		}
		_queuePrepared = continueCatalog || _parts.Count > 0;
		_catalogPreparing = continueCatalog;
		_running = !continueCatalog && _parts.Count > 0;
		_progress.Value = 0;
		_progress.Maximum = Math.Max(1, _parts.Count);
		_pause.Enabled = true;
		_pause.Text = "暂停";
		_cancel.Enabled = true;
		_status.Text = deep
			? (continueCatalog ? "当前列表保持显示；清单继续分片读取，完成后再逐个深度识别。" : "当前列表保持显示；工序在后台逐个深度识别，每片后让出界面。")
			: (continueCatalog ? "当前列表保持显示；清单继续分片读取，完成后再逐个轻量识别。" : "当前列表保持显示；轻量识别在后台分片运行。") ;
		if (!_queuePrepared && Visible && IsHandleCreated) BeginInvoke((Action)PrepareVisibleQueue);
	}

    internal void ApplySurfaceProcessLink(SurfaceProcessLinkService.Result update)
    {
        if (IsDisposed || update.Document == null) return;
        foreach (var part in _parts.Where(part => Equals(part.Document, update.Document) && part.Configuration == update.Configuration))
        {
            if (part.Row == null || part.Row.DataGridView != _grid) continue;
            if (update.Status.Length > 0) part.Row.Cells["State"].Value = update.Status;
            if (_queuedParts.Contains(part) || (_grid.IsCurrentCellInEditMode && ReferenceEquals(_grid.CurrentRow, part.Row))) continue;
            if (update.Changed)
            {
                if (part.ExistingProcessEdit != null && (part.ExistingProcessEdit.Snapshot.Raw != update.PreviousSequence || part.ExistingProcessEdit.Configuration != update.Source.Configuration)) continue;
                string draft = Convert.ToString(part.Row.Cells["Process"].Value);
                part.Row.Cells["Process"].Value = part.Edited ? SurfaceProcessLinkPolicy.MergeDraft(draft, update.PreviousSequence, update.Sequence, _config.Separator) : update.Sequence;
                part.ExistingProcessEdit = update.Source;
                part.Stamp = update.Document.GetUpdateStamp();
                if (part.TypeGroupInput != null) part.TypeGroupInput.ExistingProcess = update.Sequence;
                if (ReferenceEquals(_grid.CurrentRow, part.Row)) RefreshStepEditor();
                continue;
            }
            if (part.ExistingProcessEdit != null || update.Status.Length > 0 || update.Surface.Length == 0) continue;
            string before = Convert.ToString(part.Row.Cells["Process"].Value);
            if (!SurfaceProcessLinkPolicy.Plan(before, update.Surface, _config.Separator, part.SurfaceLink, out string sequence, out int index, out string reason))
            { if (reason.Length > 0) part.Row.Cells["State"].Value = reason; continue; }
            part.Row.Cells["Process"].Value = sequence;
            SurfaceProcessLinkPolicy.Commit(part.SurfaceLink, sequence, update.Surface, index);
            part.Row.Cells["State"].Value = "表处工序预览已同步，待确认写入";
            if (ReferenceEquals(_grid.CurrentRow, part.Row)) RefreshStepEditor();
        }
    }

    internal void PrepareForSidebar()
    {
        if (_sidebarPrepared) return;
        _sidebarPrepared = true;
        var old = Controls.Cast<Control>().ToArray();
        var buttons = ProcessSidebarLayout.Descendants(this).OfType<Button>().ToArray();
        Button Find(string text) => buttons.FirstOrDefault(b => b.Text == text);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(6) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var style in new[] { new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Absolute, 142), new RowStyle(SizeType.Absolute, 38), new RowStyle(SizeType.Percent, 100), new RowStyle(SizeType.Absolute, 0), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize) }) root.RowStyles.Add(style);
        root.Controls.Add(_summary, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        ProcessSidebarLayout.Tab(tabs, "识别与编辑", Find("重新轻量识别"), Find("当前及下属深度识别"), Find("编辑所选零件工序…"), Find("合并所选孔工序建议"), AttributeButton("定位所选零件", () => LocatePart(_grid.CurrentRow)), _pause, _cancel, _independentSteps);
        ProcessSidebarLayout.Tab(tabs, "工序组", _batchProcess, Find("套用工序组到所选行"), Find("编辑工序组…"), Find("工序查看/分析"), Find("按类型套组到所选行"));
        ProcessSidebarLayout.Tab(tabs, "属性与外购", _category, Find("应用类别"), ProcessSidebarLayout.Label("备注"), _attributeRemark, Find("备注待写入"), Find("标记所选为外购"), Find("取消所选外购（设否）"), Find("确认所选外购标记"));
        root.Controls.Add(tabs, 0, 1);
        ProcessSidebarLayout.Tab(tabs, "筛选", ProcessSidebarLayout.Label("名称或工序"), _filter, _filterState);
        var checks = ProcessSidebarLayout.Flow(AttributeButton("全选", () => SetChecks(true)), AttributeButton("全不选", () => SetChecks(false)), ProcessSidebarLayout.Label("批量操作优先使用勾选项"));
        _filter.Width = 120; _filterState.Width = 105;
        root.Controls.Add(checks, 0, 2);
        _grid.Columns["Order"].Width = 48; _grid.Columns["Part"].Width = 140;
        _grid.Columns["Process"].Width = 260;
        _grid.Columns["Configuration"].DisplayIndex = _grid.Columns.Count - 1;
        _grid.Columns["Type"].DisplayIndex = _grid.Columns.Count - 1;
        _grid.Columns["Process"].DisplayIndex = 3;
        root.Controls.Add(_grid, 0, 3);
        root.Controls.Add(_stepEditor, 0, 4);
        var status = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 }; status.Controls.Add(_progress); status.Controls.Add(_status);
        root.Controls.Add(status, 0, 5);
        var writes = ProcessSidebarLayout.Flow(Find("写入所选行"), Find("写入所有可写行"), Find("关闭")); writes.AutoSize = true;
        root.Controls.Add(writes, 0, 6);
        foreach (var control in old) { Controls.Remove(control); control.Dispose(); }
        MinimumSize = Size.Empty; Controls.Add(root); ProcessSidebarLayout.Wrap(root, _summary, _status);
        ProcessSidebarLayout.AutoHeightTabs(root, 1, tabs);
        RefreshStepEditor();
    }

    private void SetChecks(bool check)
    {
        foreach (DataGridViewRow row in _grid.Rows) if (row.Visible) row.Cells["Checked"].Value = check;
        _status.Text = check ? "已勾选当前可见零件；批量操作优先使用勾选项。" : "已清除当前可见勾选；未勾选时使用高亮所选行。";
    }

    private void RefreshStepEditor()
    {
        if (!_sidebarPrepared || _buildingStepEditor) return;
        _buildingStepEditor = true;
        try
        {
            foreach (Control old in _stepEditor.Controls.Cast<Control>().ToArray()) old.Dispose();
            bool show = _independentSteps.Checked && _grid.CurrentRow?.Tag is BatchPart;
            _stepEditor.Visible = show;
            if (_stepEditor.Parent is TableLayoutPanel root) root.RowStyles[4].Height = show ? 128 : 0;
            if (!show) return;
            var row = _grid.CurrentRow; var part = (BatchPart)row.Tag;
            if (Convert.ToString(row.Cells["Type"].Value) == "识别中") { _stepEditor.Controls.Add(ProcessSidebarLayout.Label("此零件正在识别，完成后显示逐道工序。")); return; }
            var steps = ProcessSequenceEditorWindow.Split(Convert.ToString(row.Cells["Process"].Value), _config.Separator).ToList();
            if (steps.Count == 0) steps.Add("");
            for (int i = 0; i < steps.Count; i++)
            {
                int index = i;
                var block = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(1) };
                var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 128, Text = steps[index] };
                choice.Items.AddRange(ProcessSidebarLayout.Choices(_config).Concat(steps).Distinct().Cast<object>().ToArray());
                choice.SelectionChangeCommitted += (_, __) => { steps[index] = choice.Text; ApplyInlineSteps(row, part, steps); };
                choice.Validated += (_, __) => { if (!_buildingStepEditor) { steps[index] = choice.Text; ApplyInlineSteps(row, part, steps); } };
                block.Controls.Add(ProcessSidebarLayout.Label((index + 1).ToString())); block.Controls.Add(choice);
                block.Controls.Add(AttributeButton("+", () => { steps.Insert(index + 1, "新工序"); if (ApplyInlineSteps(row, part, steps)) RefreshStepEditor(); }));
                block.Controls.Add(AttributeButton("−", () => { steps.RemoveAt(index); if (ApplyInlineSteps(row, part, steps)) RefreshStepEditor(); }));
                _stepEditor.Controls.Add(block);
            }
        }
        finally { _buildingStepEditor = false; }
    }

    private bool ApplyInlineSteps(DataGridViewRow row, BatchPart part, IList<string> steps)
    {
        if (_buildingStepEditor || _processingSlice || row.DataGridView != _grid || !ReferenceEquals(row.Tag, part)) return false;
        if (steps.Any(step => string.IsNullOrWhiteSpace(step) || step.Contains(_config.Separator))) { _status.Text = "每项填写一道工序，名称不可为空或包含分隔符。"; return false; }
        var remaining = _manualWrites.Where(write => !ReferenceEquals(write.Part, part)).ToArray();
        _manualWrites.Clear(); foreach (var write in remaining) _manualWrites.Enqueue(write); _queuedParts.Remove(part);
        row.Cells["Process"].Value = string.Join(_config.Separator, steps);
        part.Edited = part.ProcessEditedManually = true;
        OnProcessEdited(this, new DataGridViewCellEventArgs(_grid.Columns["Process"].Index, row.Index));
        row.Cells["State"].Value = "逐道工序已调整，待确认写入";
        _status.Text = "已修改本行草稿；底部写入按钮确认后才更新模型。";
        return true;
    }

    private void OnPartCellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        string column = _grid.Columns[e.ColumnIndex].Name;
        if (column == "Process") EditSelectedPartSequence();
        else if (column == "Part") EditPartSequence(_grid.Rows[e.RowIndex], true);
    }

    private void LocatePart(DataGridViewRow row)
    {
        if (!(row?.Tag is BatchPart part) || _processingSlice) return;
        try
        {
            if (!IsAssemblyContextActive()) throw new InvalidOperationException("请先返回本装配体及原配置。");
            if (part.IsRoot) { _status.Text = "当前行就是本装配体。"; return; }
            var assembly = (IAssemblyDoc)_assemblyDocument;
            IComponent2 component = assembly.GetComponentByName(part.ComponentName);
            if (component == null || component.IsSuppressed()
                || !string.Equals(component.GetPathName() ?? "", part.Path, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(component.ReferencedConfiguration ?? "", part.Configuration, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("原组件已变化或不可用，请刷新装配体清单后定位。");
            if (!component.Select4(false, null, false)) throw new InvalidOperationException("未能选中对应零件。");
            _assemblyDocument.ViewZoomToSelection();
            _onCompleted?.Invoke();
            _status.Text = "已定位零件：" + part.Name + " / " + part.Configuration;
        }
        catch (Exception ex) { _status.Text = "定位失败：" + ex.Message; Logger.Error("Locate process row component failed", ex); }
    }

    private void EditSelectedPartSequence() => EditPartSequence(_grid.CurrentRow, false);

    private void EditPartSequence(DataGridViewRow row, bool saveToModel)
    {
        if (_processingSlice) return;
        if (!_grid.EndEdit()) return;
        if (row == null || !(row.Tag is BatchPart part)) { _status.Text = "请先选择一个零件行。"; return; }
        if (Convert.ToString(row.Cells["Type"].Value) == "识别中") { _status.Text = "该零件仍在后台识别，完成后即可编辑。"; return; }
        if (!_editingParts.Add(part)) { _status.Text = "此零件的工序编辑窗口已打开。"; return; }
        string snapshot = Convert.ToString(row.Cells["Process"].Value);
        object[] rowSnapshot = row.Cells.Cast<DataGridViewCell>().Select(cell => cell.Value).ToArray();
        int? stamp = part.Stamp;
        string configuration = part.Configuration;
        var editor = new ProcessSequenceEditorWindow(part.Name, snapshot, _config.Separator, ProcessSidebarLayout.Choices(_config), _independentSteps.Checked,
            saveToModel ? (Func<ProcessModelPreview>)(() => ProcessModelPreview.Read(_getSolidWorks(), part.Path, part.Configuration)) : null,
            saveToModel ? "保存并写入" : "应用到草稿");
        editor.ShowModeless(this, result =>
        {
            _editingParts.Remove(part);
            if (result != DialogResult.OK || IsDisposed) return;
            if (row.DataGridView != _grid || !ReferenceEquals(row.Tag, part) || !_parts.Contains(part)
                || part.Stamp != stamp || part.Configuration != configuration
                || !rowSnapshot.SequenceEqual(row.Cells.Cast<DataGridViewCell>().Select(cell => cell.Value))
                || Convert.ToString(row.Cells["Process"].Value) != snapshot || _processingSlice)
            { _status.Text = "原零件工序已变化或正在写入，未覆盖草稿；请重新打开编辑。"; return; }
            // Replacing this draft invalidates only this row's previously confirmed write.
            var remaining = _manualWrites.Where(write => !ReferenceEquals(write.Part, part)).ToArray();
            _manualWrites.Clear(); foreach (var write in remaining) _manualWrites.Enqueue(write);
            _queuedParts.Remove(part);
            row.Cells["Process"].Value = editor.Sequence;
            if (part.ExistingProcessEdit == null) { part.PendingProperties.Remove(_config.CraftDescriptionPropertyName); part.PendingProperties.Remove(_config.SummaryPropertyName); }
            part.ProcessEditedManually = true;
            part.Edited = true;
            if (editor.Sequence.Length > 0) OnProcessEdited(this, new DataGridViewCellEventArgs(_grid.Columns["Process"].Index, row.Index));
            row.Cells["State"].Value = "工序草稿已调整，模型尚未修改";
            _status.Text = "仅修改了“" + part.Name + "”的工序草稿；请检查后再写入所选行。";
            RefreshStepEditor();
            if (saveToModel) QueueRows(false, new[] { row });
        });
    }

	private void OnProcessEdited(object sender, DataGridViewCellEventArgs e)
	{
		if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Process") return;
		DataGridViewRow row = _grid.Rows[e.RowIndex];
		if (!(row.Tag is BatchPart part)) return;
		string process = NormalizeManualProcess(row.Cells["Process"].Value?.ToString());
		if (process.Length == 0) return;
		row.Cells["Process"].Value = process;
		// Editing the sequence must not erase a group/category value already staged
		// for this row. The global selector may have changed after applying a group.
		if (part.ExistingProcessEdit == null) part.PendingProperties.Remove(_config.CraftDescriptionPropertyName);
		part.ProcessEditedManually = true;
		part.Edited = true;
		if (new[] { "失败", "待深度", "识别中", "未加载" }.Contains(Convert.ToString(row.Cells["Type"].Value))) row.Cells["Type"].Value = "人工确认";
		row.Cells["State"].Value = "工序已修改，待确定写入";
	}

	private void QueueSelectedWrites() => QueueWrites(false);
	private void QueueAllWrites() => QueueWrites(true);
	private void QueueWrites(bool all) => QueueRows(all, null);
	private void QueueRows(bool all, IEnumerable<DataGridViewRow> explicitRows)
	{
		if (!_grid.EndEdit()) return;
		if (all && (!_queuePrepared || _catalogPreparing)) { _status.Text = "装配体清单尚未读取完成，请等待清单完整后再全部确定。"; return; }
		if (!IsAssemblyContextActive()) { _status.Text = "请先返回本装配体及原配置，未排队写入。"; return; }
		KeyValuePair<string, string>? selectedGroup = SelectedProcessGroup();
		string batchProcess = selectedGroup.HasValue ? NormalizeManualProcess(selectedGroup.Value.Value) : "";
		IEnumerable<DataGridViewRow> selectedRows = explicitRows ?? (all ? _grid.Rows.Cast<DataGridViewRow>() : SelectedVisiblePartRows());
		if (!selectedRows.Any() && _grid.CurrentRow != null) selectedRows = new[] { _grid.CurrentRow };
		int count = 0;
		int considered = 0;
		foreach (DataGridViewRow row in selectedRows.Where(row => (all || row.Visible) && row.Tag is BatchPart))
		{
			considered++;
			BatchPart part = (BatchPart)row.Tag;
			if (_queuedParts.Contains(part)) continue;
			if (part.Document != null && part.Document.IsOpenedReadOnly()) { row.Cells["State"].Value = "文件只读，未排队写入。"; continue; }
			if (all && !selectedGroup.HasValue && (part.Revalidate || (!part.Edited && new[] { "待深度", "识别中", "未加载", "失败", "多配置" }.Contains(Convert.ToString(row.Cells["Type"].Value))))) { row.Cells["State"].Value = "尚无可确认结果，请完成识别或手工填写后再确定。"; continue; }
			if (part.ReferencedConfigurations.Count > 1) { row.Cells["State"].Value = "该行对应多个引用配置，请重新读取配置清单后分别处理。"; continue; }
			if (part.Document != null && part.Stamp.HasValue && !PartUnchanged(part)) { row.Cells["State"].Value = "模型已变化，请重新读取；草稿已保留。"; continue; }
			bool applyGroup = selectedGroup.HasValue && !part.ProcessEditedManually && part.ExistingProcessEdit == null;
			string process = applyGroup
				? batchProcess
				: NormalizeManualProcess(row.Cells["Process"].Value?.ToString());
            if (part.ExistingProcessEdit != null)
            {
                var source = part.ExistingProcessEdit;
                if (!string.IsNullOrWhiteSpace(part.SnapshotFileStamp) && part.SnapshotFileStamp != FileStamp(part.Path)) { row.Cells["State"].Value = "来源文件已变化，已有工序编辑未排队"; continue; }
                _manualWrites.Enqueue(new ManualWrite { Part = part, Row = row, Process = process, ProcessEdit = source, Values = new Dictionary<string,string>(part.PendingProperties, StringComparer.OrdinalIgnoreCase),
                    Stamp = part.Document?.GetUpdateStamp(), Configuration = source.Configuration, FileStamp = FileStamp(part.Path) });
                _queuedParts.Add(part); part.Edited = true; count++;
                row.Cells["State"].Value = "已确认修改原" + source.ScopeLabel + "工序，等待回读校验写入";
                continue;
            }
			if (process.Length > 0)
			{
				row.Cells["Process"].Value = process;
				try
				{
					string stagedGroup;
					string groupName = applyGroup ? selectedGroup.Value.Key : (part.PendingProperties.TryGetValue(_config.ProcessTypePropertyName, out stagedGroup) ? stagedGroup : "");
					Dictionary<string, string> merged = part.HoleOnlyIncrement ? new Dictionary<string,string>(part.PendingProperties, StringComparer.OrdinalIgnoreCase) : ProcessRecognitionService.MergeManualProcessGroupValues(part.PendingProperties, process, groupName, _config);
					if (part.HoleOnlyIncrement) merged[_config.SummaryPropertyName] = process;
					part.PendingProperties.Clear();
					foreach (KeyValuePair<string, string> property in merged) part.PendingProperties[property.Key] = property.Value;
				}
				catch (InvalidOperationException ex) { row.Cells["State"].Value = ex.Message; continue; }
			}
			var values = new Dictionary<string, string>(part.PendingProperties, StringComparer.OrdinalIgnoreCase);
			foreach (var suggestion in part.HoleOnlyIncrement ? Array.Empty<KeyValuePair<string,string>>() : ProcessPartClassification.PropertySuggestion(part.AnalysisInput, _config))
				if (!values.ContainsKey(suggestion.Key)) values[suggestion.Key] = suggestion.Value;
			bool queued = values.Count > 0;
			if (queued)
			{
				_manualWrites.Enqueue(new ManualWrite { Part = part, Row = row, Process = process, Values = values, Stamp = part.Document?.GetUpdateStamp(), Configuration = part.Configuration, FileStamp = FileStamp(part.Path) });
				_queuedParts.Add(part); part.Edited = true;
			}
			row.Cells["State"].Value = queued ? "写入已确认并排队" : "没有可写入的内容";
			if (queued) count++;
		}
		_status.Text = count > 0
			? "已确认 " + count + " 个文件/配置行；SOLIDWORKS 主线程分片逐行写入，结果见每行状态。"
			: "没有新排队的写入；请检查选中行及状态列。";
		if (count > 0) { _cancelled = false; _paused = false; _pause.Enabled = _cancel.Enabled = true; _pause.Text = "暂停"; }
		if (count > 0 && selectedGroup.HasValue) _status.Text += " 使用工序组“" + selectedGroup.Value.Key + "”。";
		if (all) _status.Text = "写入所有可写行：" + _status.Text + "另有 " + (considered-count) + " 项未新增排队（已排队或暂不可写）。";
		if (count > 0) _status.Text += " 点击“取消任务”可停止尚未执行的项目。";
	}

	private void ExpandInteractiveCatalog()
	{
		try
		{
			if (!(_assemblyDocument is IAssemblyDoc assembly)) throw new InvalidOperationException("当前文档不是装配体。");
			object value = assembly.GetComponents(false);
			_componentItems = value is Array components ? components.Cast<object>().ToArray() : Array.Empty<object>();
			_componentScanIndex = 0;
			_instanceCount = 0;
			_unavailableCount = 0;
			_catalogIsPartial = false;
			_fullCatalogPending = false;
			_queuePrepared = true;
			_catalogPreparing = true;
			_cancelled = false;
			_progress.Maximum = Math.Max(1, _componentItems.Length);
			_progress.Value = 0;
			_status.Text = "正在补全完整装配体清单；已有识别结果和人工修改会保留。";
			ProcessCatalogSlice();
		}
		catch (Exception ex)
		{
			_catalogPreparing = false;
			_fullCatalogPending = false;
			Logger.Error("Assembly process recognition catalog expansion failed", ex);
			_status.Text = "完整清单读取失败；原有识别结果已保留，可重新轻量识别。";
		}
	}

	private ProcessRecognitionInput ReadPartForReferencedConfiguration(BatchPart part, IModelDoc2 document, bool openedForBatch, bool wasDirty)
	{
		string target = (part.Configuration ?? "").Trim();
		string active = document.ConfigurationManager?.ActiveConfiguration?.Name ?? "";
		if (!ProcessConfigurationReadPolicy.CanReadTarget(active, target, wasDirty, openedForBatch, out string refusal))
			throw new InvalidOperationException(refusal);
		if (target.Length == 0 || string.Equals(active, target, StringComparison.OrdinalIgnoreCase))
		{
			ProcessRecognitionInput direct = ReadWithWorkload(document, part.Name);
			direct.DocumentTitle = part.Name;
			if (!wasDirty && document.GetSaveFlag()) throw new InvalidOperationException("识别期间模型状态变为未保存；为避免意外保存，该行已停止并保留模型供检查。");
			return direct;
		}
		if (!document.ShowConfiguration2(target))
			throw new InvalidOperationException("无法临时切换到引用配置“" + target + "”。");
		ProcessRecognitionInput input;
		try
		{
			input = ReadWithWorkload(document, part.Name);
			input.DocumentTitle = part.Name;
		}
		finally
		{
			if (!document.ShowConfiguration2(active))
				throw new InvalidOperationException("识别后无法恢复原配置“" + active + "”；模型未保存，请检查当前配置。");
		}
		if (!wasDirty && document.GetSaveFlag()) throw new InvalidOperationException("识别期间模型状态变为未保存；为避免意外保存，该行已停止并保留模型供检查。");
		return input;
	}
	private sealed class RecognitionDeclinedException : OperationCanceledException { }
	private ProcessRecognitionInput ReadWithWorkload(IModelDoc2 document, string name)
	{
		_workloadReadNotice = ""; _effectiveReadDeep = _deepMode || _readAutomaticDeep;
		if (EffectiveDeep)
		{
			var workload = ProcessRecognitionWorkload.Read(document);
			if (!_deepMode && !workload.AllowAutomaticDeep) { _effectiveReadDeep = false; _workloadReadNotice = ProcessRecognitionWorkload.AutomaticFallbackText; }
			else if (_deepMode && !_deepWorkloadConfirmed && !workload.Confirm(this, "零件“" + name + "”深度识别", RecognitionConfirmationForTest)) throw new RecognitionDeclinedException();
		}
		return SolidWorksProcessFeatureReader.Read(document, deep: EffectiveDeep, _config, maxGeometryFaces: _deepMode ? 1200 : 600);
	}

	private KeyValuePair<string, string>? SelectedProcessGroup()
	{
		if (!(_batchProcess.SelectedItem is KeyValuePair<string, string> pair) || string.IsNullOrWhiteSpace(pair.Value)) return null;
		return pair;
	}

	private void StageSelectedProcessGroup()
	{
		if (!_grid.EndEdit()) return;
		KeyValuePair<string, string>? group = SelectedProcessGroup();
		if (!group.HasValue) { _status.Text = "请选择一个已配置的工序组。"; return; }
		string sequence = NormalizeManualProcess(group.Value.Value);
		if (sequence.Length == 0) { _status.Text = "工序组没有可写入的工序。"; return; }
		IReadOnlyList<KeyValuePair<string, string>> groupValues;
		try { groupValues = ProcessRecognitionService.BuildManualProcessGroupValues(sequence, group.Value.Key, _config); }
		catch (InvalidOperationException ex) { _status.Text = ex.Message; return; }
		IEnumerable<DataGridViewRow> rows = SelectedVisiblePartRows();
		if (!rows.Any() && _grid.CurrentRow != null) rows = new[] { _grid.CurrentRow };
		int count = 0;
		foreach (DataGridViewRow row in rows.Where(item => item.Visible && item.Tag is BatchPart))
		{
			BatchPart part = (BatchPart)row.Tag;
			if (_queuedParts.Contains(part)) continue;
			foreach (KeyValuePair<string, string> property in groupValues) part.PendingProperties[property.Key] = property.Value;
			part.ProcessEditedManually = true;
			row.Cells["Process"].Value = sequence;
			row.Cells["State"].Value = "已套用工序组“" + group.Value.Key + "”；可直接改工序明细，再确认写入。";
			part.Edited = true;
			count++;
		}
		_status.Text = count > 0 ? "已将工序组“" + group.Value.Key + "”填入 " + count + " 行；现在可在表格修改工序明细，然后点“写入所选行”保存到模型。" : "没有可套用工序组的选中零件。先在表格中选中要处理的行。";
	}
    private void StageSelectedTypeGroups()
    {
        if (!_grid.EndEdit()) return;
        if (_manualWrites.Count > 0 || _processingSlice) { _status.Text = "请先完成或取消已确认的写入，再套用类型组。"; return; }
        var rows = SelectedVisiblePartRows();
        if (rows.Length == 0 && _grid.CurrentRow?.Tag is BatchPart && _grid.CurrentRow.Visible) rows = new[] { _grid.CurrentRow };
        int applied = 0, skipped = 0;
        foreach (DataGridViewRow row in rows)
        {
            var part = (BatchPart)row.Tag;
            string reason = "";
            if (_queuedParts.Contains(part) || part.Revalidate || part.ReferencedConfigurations.Count > 1) reason = "结果待复核或已排队，未套用";
            else if (part.ExistingProcessEdit != null) reason = "模型已有工序，类型默认组未覆盖";
            else if (part.Edited || part.ProcessEditedManually || part.PendingProperties.Count > 0) reason = "已有人工草稿，类型默认组未覆盖";
            else if (part.TypeGroupInput == null) reason = "尚未读取明确类型，请重新识别后套用";
            else if (!string.IsNullOrWhiteSpace(part.TypeGroupInput.ExistingProcess)) reason = "模型已有工序，类型默认组未覆盖";
            else if (ProcessRecognitionConfig.ResolveCombination(_config, part.TypeGroupInput.ProcessType).Count > 0) reason = "模型已有明确工序组，默认组未覆盖";
            var match = reason.Length == 0 ? ProcessTypeGroupPolicy.Resolve(_config, part.TypeGroupInput) : null;
            if (match != null && !match.Matched) reason = match.Reason;
            if (reason.Length > 0) { row.Cells["State"].Value = reason; skipped++; continue; }
            string sequence = ProcessRecognitionService.Join(ProcessRecognitionConfig.ResolveCombination(_config, match.Group).Select(step => step.Name), _config.Separator);
            try
            {
                var values = ProcessRecognitionService.BuildManualProcessGroupValues(sequence, match.Group, _config);
                foreach (var value in values) part.PendingProperties[value.Key] = value.Value;
                row.Cells["Process"].Value = sequence;
                row.Cells["State"].Value = match.Reason + " → 工序组“" + match.Group + "”，仅填入草稿，待确认写入";
                part.Edited = true; part.ProcessEditedManually = true; applied++;
            }
            catch (InvalidOperationException ex) { row.Cells["State"].Value = ex.Message; skipped++; }
        }
        _status.Text = "按类型套组：已填入 " + applied + " 个所选零件草稿，跳过 " + skipped + " 个；模型尚未修改，请检查每行状态后写入所选行。";
    }

	private void EditRules()
	{
		if (_manualWrites.Count > 0 || _processingSlice) { _status.Text = "请先完成或取消已确认的写入，再编辑规则。"; return; }
		if (OwnedForms.OfType<ProcessRulesWindow>().Any()) { OwnedForms.OfType<ProcessRulesWindow>().First().Activate(); return; }
		bool paused = _paused; _paused = true;
		var dialog = new ProcessRulesWindow(_config);
		dialog.ShowModeless(this, result => {
			try { if (result != DialogResult.OK) return; if (SaveRules == null) throw new InvalidOperationException("请从主设置页编辑类别与组合。");
				SaveRules(dialog.Result); ProcessRulesWindow.CopyRules(dialog.Result, _config);
				_cache.Save(); _cache = new ProcessRecognitionBatchCache(_config);
				_category.Items.Clear(); _category.Items.AddRange(ProcessRecognitionConfig.ParseProcessNameMappings(_config.ProcessCategories).Select(pair => (object)pair).ToArray()); if (_category.Items.Count > 0) _category.SelectedIndex=0;
				_batchProcess.Items.Clear();
				_batchProcess.Items.Add(new KeyValuePair<string, string>("按各行工序", ""));
				_batchProcess.Items.AddRange(ProcessRecognitionConfig.ParseCombinations(_config.ProcessCombinations).Cast<object>().ToArray());
				_batchProcess.SelectedIndex = 0;
				Restart(_deepMode); _status.Text="规则已保存，手工草稿保留；已识别的其他项目将按新规则更新。";
			} catch (Exception ex) { _status.Text=ex.Message; Logger.Error("Save assembly process rules failed", ex); } finally { _paused=paused; }
		});
	}
	private void ShowAnalysis()
	{
		if (!_grid.EndEdit()) return;
		var open = OwnedForms.OfType<ProcessAnalysisWindow>().FirstOrDefault(); if (open != null) { open.Activate(); return; }
		var part = _grid.CurrentRow?.Tag as BatchPart;
		var rows = part?.Row == null ? Array.Empty<RecognizedProcess>() : ProcessSequenceEditorWindow.Split(Convert.ToString(part.Row.Cells["Process"].Value), _config.Separator)
			.Select(name => new RecognizedProcess { Name = name, Confidence = 0, Evidence = part.Edited ? "当前人工草稿" : "当前列表工序（已有属性或识别/缓存结果）" }).ToArray();
		new ProcessAnalysisWindow(_config, part?.AnalysisInput, part?.Deep ?? false, rows).ShowModeless(this);
	}

	private void StageSelectedPropertyWrites(string propertyName, string value)
	{
		string name = (propertyName ?? "").Trim();
		string text = (value ?? "").Trim();
		if (name.Length == 0 || text.Length == 0)
		{
			_status.Text = name.Length == 0 ? "属性名为空，请先在主设置中配置。" : "请先输入要写入的备注值。";
			return;
		}
		IEnumerable<DataGridViewRow> rows = SelectedVisiblePartRows();
		if (!rows.Any() && _grid.CurrentRow != null) rows = new[] { _grid.CurrentRow };
		int count = 0;
		foreach (DataGridViewRow row in rows.Where(row => row.Tag is BatchPart))
		{
			BatchPart part = (BatchPart)row.Tag;
			if (_queuedParts.Contains(part)) continue;
			part.PendingProperties[name] = text;
			if (string.Equals(name, _config.ProcessTypePropertyName, StringComparison.OrdinalIgnoreCase))
			{
				var combination = ProcessRecognitionConfig.ResolveCombination(_config, text);
				if (combination.Count > 0) { row.Cells["Process"].Value = ProcessRecognitionService.Join(combination.Select(step => step.Name), _config.Separator); part.Edited = true; }
			}
			row.Cells["State"].Value = name + "=" + text + " 待确定写入";
			count++;
		}
			_status.Text = "已生成 " + count + " 个待写入项；模型尚未修改，请点击“写入所选行”。";
	}

	private string NormalizeManualProcess(string value)
	{
		if (new[] { "识别中", "待识别", "未加载", "失败" }.Contains((value ?? "").Trim())) return "";
		return ProcessRecognitionService.NormalizeManualProcessSequence(value, _config.Separator);
	}

    private DataGridViewRow[] SelectedVisiblePartRows()
    {
        var checkedRows = _grid.Rows.Cast<DataGridViewRow>().Where(row => row.Visible && row.Tag is BatchPart && Convert.ToBoolean(row.Cells["Checked"].Value ?? false)).ToArray();
        if (checkedRows.Length > 0) return checkedRows;
        var rows = _grid.SelectedRows.Cast<DataGridViewRow>().Where(row => row.Visible && row.Tag is BatchPart).ToArray();
        return rows.Length == 0 && _grid.CurrentRow?.Tag is BatchPart && _grid.CurrentRow.Visible ? new[] { _grid.CurrentRow } : rows;
    }

    private void StageSelectedPurchaseMarkers(bool purchased)
    {
        if (!_grid.EndEdit() || _processingSlice) return;
        int count = 0;
        foreach (var row in SelectedVisiblePartRows())
        {
            var part = (BatchPart)row.Tag;
            if (_queuedParts.Contains(part) || part.Revalidate || part.TypeGroupInput?.PurchaseMarkerSnapshot == null || part.ReferencedConfigurations.Count > 1)
            { row.Cells["State"].Value = "请先重新识别本零件的引用配置；已排队/多配置/未核实项目未标记"; continue; }
            part.PendingPurchase = purchased;
            part.PendingPurchaseSnapshot = part.TypeGroupInput.PurchaseMarkerSnapshot;
            row.Cells["Purchase"].Value = PurchasedPartPolicy.Display(part.TypeGroupInput) + " → 待确认：" + (purchased ? "是" : "否");
            row.Cells["State"].Value = "只改引用配置“" + part.Configuration + "”的外购标记，请点击“确认所选外购标记”；不写工序/类型";
            count++;
        }
        _status.Text = "已暂存 " + count + " 个所选行的外购标记，模型尚未修改。请检查外购身份列再独立确认。";
    }

    private void QueueSelectedPurchaseMarkers()
    {
        if (!_grid.EndEdit() || !IsAssemblyContextActive() || _processingSlice) return;
        int count = 0;
        foreach (var row in SelectedVisiblePartRows())
        {
            var part = (BatchPart)row.Tag;
            if (!part.PendingPurchase.HasValue || _queuedParts.Contains(part)) continue;
            if (part.Revalidate || part.ReferencedConfigurations.Count > 1 || part.PendingPurchaseSnapshot == null
                || (!string.IsNullOrWhiteSpace(part.SnapshotFileStamp) && part.SnapshotFileStamp != FileStamp(part.Path))
                || (part.Document != null && (part.Document.IsOpenedReadOnly() || (part.Stamp.HasValue && !PartUnchanged(part)))))
            { row.Cells["State"].Value = "外购标记预览已过期或零件不可写，请重新识别后确认"; continue; }
            _manualWrites.Enqueue(new ManualWrite { Part = part, Row = row, PurchaseOnly = true, PurchaseValue = part.PendingPurchase.Value,
                PurchaseSnapshot = part.PendingPurchaseSnapshot, Values = new Dictionary<string,string>(), Stamp = part.Document?.GetUpdateStamp(), Configuration = part.Configuration, FileStamp = FileStamp(part.Path) });
            _queuedParts.Add(part); row.Cells["State"].Value = "已确认，仅外购标记排队写入"; count++;
        }
        if (count > 0) { _cancelled = false; _paused = false; _pause.Enabled = _cancel.Enabled = true; _pause.Text = "暂停"; }
        _status.Text = "已确认 " + count + " 个所选引用配置的外购标记；工序及其他待写草稿保持不变，取消任务可停止未执行项。";
    }

	private void ProcessManualWrite(ManualWrite write)
	{
		IModelDoc2 document = null;
		bool openedForBatch = false;
		try
		{
			document = ResolveDocument(write.Part, out openedForBatch);
			if (document == null) throw new InvalidOperationException("文件无法在后台打开");
			if (document.IsOpenedReadOnly()) throw new InvalidOperationException("文件为只读，未写入属性");
			if ((!string.IsNullOrWhiteSpace(write.Configuration) && document.GetConfigurationByName(write.Configuration) == null)
				|| (write.Stamp.HasValue && !openedForBatch && document.GetUpdateStamp() != write.Stamp.Value)
				|| (write.FileStamp != FileStamp(write.Part.Path))) throw new InvalidOperationException("排队后模型或配置已变化，请重新读取后确认");
			bool wasDirty = document.GetSaveFlag();
			if (write.PurchaseOnly && document.GetActiveSketch2() != null) throw new InvalidOperationException("零件正在编辑草图，未写入外购标记");
			var store = new PropertyWriteBatch.ModelStore(document, write.Configuration);
			int changed = write.PurchaseOnly ? PurchasedPartPolicy.Apply(store, write.PurchaseSnapshot, write.PurchaseValue)
                : write.ProcessEdit != null ? write.ProcessEdit.Apply(store, write.Process, write.Values) : PropertyWriteBatch.Apply(store, write.Values);
			bool saved = false;
			if (changed > 0 && !wasDirty && !string.IsNullOrWhiteSpace(document.GetPathName()))
			{
				int errors = 0;
				int warnings = 0;
				saved = document.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
			}
			string state = changed == 0 ? "属性未变化，无需重复写入" : saved ? "已写入并保存 " + changed + " 项属性" : (wasDirty ? "已写入；保留原未保存状态" : "已写入，保存未完成，请手动保存模型");
			if (write.PurchaseOnly)
			{
				write.Part.PendingPurchase = null; write.Part.PendingPurchaseSnapshot = null;
				if (write.Part.TypeGroupInput != null) { write.Part.TypeGroupInput.Purchase = write.PurchaseValue ? "是" : "否"; write.Part.TypeGroupInput.PurchaseMarkerSnapshot = PurchasedPartPolicy.ReadSnapshot(document, write.Configuration); }
				write.Row.Cells["Purchase"].Value = PurchasedPartPolicy.Display(write.Part.TypeGroupInput);
				state += "；仅外购标记，工序/类型和人工草稿保持不变";
			}
			else if (write.ProcessEdit != null)
            {
                write.Part.ExistingProcessEdit = ExistingProcessEditTarget.Read(document, write.Configuration, write.ProcessEdit.PropertyName);
                foreach (var written in write.Values) if (write.Part.PendingProperties.TryGetValue(written.Key, out string pending) && pending == written.Value) write.Part.PendingProperties.Remove(written.Key);
                write.Part.Edited = write.Part.PendingProperties.Count > 0;
                state += "；已修改原" + write.ProcessEdit.ScopeLabel + "工序，其他属性草稿保留";
            }
            else
            {
                write.Part.PendingProperties.Clear(); write.Part.Edited = false;
                if (write.Values.ContainsKey(_config.SummaryPropertyName)) write.Part.ExistingProcessEdit = ExistingProcessEditTarget.Read(document, write.Configuration, _config.SummaryPropertyName);
            }
			if (!write.PurchaseOnly && write.Part.TypeGroupInput != null) write.Part.TypeGroupInput.ExistingProcess = write.Process;
			write.Part.Stamp = document.GetUpdateStamp();
			write.Part.SnapshotFileStamp = FileStamp(write.Part.Path);
			RefreshQueuedWriteSnapshots(write.Part.Path, document);
			if (openedForBatch && (saved || changed == 0))
			{
				_getSolidWorks()?.CloseDoc(document.GetTitle());
				write.Part.Document = null;
			}
			else if (openedForBatch && !saved) document.Visible = true;
			if (!write.Row.IsNewRow) write.Row.Cells["State"].Value = state;
			_status.Text = write.Part.Name + "：" + state;
			_onCompleted?.Invoke();
		}
		catch (Exception ex)
		{
			if (openedForBatch && document != null)
			{
				try { if (!document.GetSaveFlag()) _getSolidWorks()?.CloseDoc(document.GetTitle()); else document.Visible = true; }
				catch { }
			}
			if (!write.Row.IsNewRow) write.Row.Cells["State"].Value = "写入失败：" + ex.Message;
			Logger.Error("Confirmed batch property write failed", ex);
		}
	}

	private static string FileStamp(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return "";
		var file = new System.IO.FileInfo(path); return file.Length + ":" + file.LastWriteTimeUtc.Ticks;
	}
	private void RefreshQueuedWriteSnapshots(string path, IModelDoc2 document)
	{
		if (_manualWrites.Count == 0) return;
		string fileStamp = FileStamp(path);
		int? stamp = document?.GetUpdateStamp();
		ManualWrite[] pending = _manualWrites.ToArray();
		_manualWrites.Clear();
		foreach (ManualWrite write in pending)
		{
			if (string.Equals(write.Part.Path, path, StringComparison.OrdinalIgnoreCase))
			{
				write.FileStamp = fileStamp;
				write.Stamp = stamp;
			}
			_manualWrites.Enqueue(write);
		}
	}
	private void CancelPending()
	{
		_cancelled = true; _paused = false; _running = false;
		foreach (var write in _manualWrites) write.Row.Cells["State"].Value = "排队写入已取消，草稿已保留";
		_manualWrites.Clear(); _queuedParts.Clear();
		_pause.Text = "暂停"; _pause.Enabled = _cancel.Enabled = false;
		_status.Text = "任务已取消；已完成内容保留，尚未执行的写入已停止。";
		_cache.Save();
	}
	private void FilterRows()
	{
		if (_grid.IsCurrentCellInEditMode || _processingSlice) return;
		_grid.CurrentCell = null;
		foreach (DataGridViewRow row in _grid.Rows)
		{
			string text = string.Join(" ", row.Cells.Cast<DataGridViewCell>().Select(cell => cell.Value?.ToString() ?? ""));
			string state = row.Cells["State"].Value?.ToString() ?? "";
			bool status = _filterState.SelectedIndex <= 0 || (_filterState.SelectedIndex == 1 && (state.Contains("待") || state.Contains("草稿")))
				|| (_filterState.SelectedIndex == 2 && (text.Contains("失败") || text.Contains("跳过") || text.Contains("未加载") || text.Contains("多配置") || text.Contains("待深度")))
				|| (_filterState.SelectedIndex == 3 && state.Contains("已写入"));
			row.Visible = status && text.IndexOf(_filter.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
		}
	}

	private static string DocumentKey(IModelDoc2 document)
	{
		string path = document?.GetPathName() ?? "";
		return path.Length > 0 ? path : document?.GetTitle() ?? "";
	}

	private static string PartConfigurationKey(string path, string configuration)
		=> (path ?? "").Trim() + "\n" + (configuration ?? "").Trim();

	private void TogglePause()
	{
		_paused = !_paused;
		_pause.Text = _paused ? "继续" : "暂停";
		if (_paused) _status.Text = "后台批量识别已暂停。";
	}

	private void OnFormClosing(object sender, FormClosingEventArgs e)
	{
		if (!_shutdown && e.CloseReason == CloseReason.UserClosing)
		{
			_grid.EndEdit();
			e.Cancel = true;
            Hide();
            SidebarCloseRequested?.Invoke();
            _cache.Save();
			return;
		}
		_cancelled = true;
		_running = false;
		_cache.Save();
	}

	private sealed class BatchPart
	{
        internal readonly SurfaceProcessLinkState SurfaceLink = new SurfaceProcessLinkState();
		public ExistingProcessEditTarget ExistingProcessEdit { get; set; }
		public bool? PendingPurchase { get; set; }
		public PropertyWriteBatch.Snapshot PendingPurchaseSnapshot { get; set; }
		public ProcessRecognitionInput TypeGroupInput { get; set; }
		public ProcessRecognitionInput AnalysisInput { get; set; }
		public IModelDoc2 Document { get; set; }
		public string Path { get; set; } = "";
        public string ComponentName { get; set; } = "";
		public string Name { get; set; } = "";
		public string CachedClassification { get; set; } = "";
		public string CachedProcess { get; set; } = "";
		public string SnapshotFileStamp { get; set; } = "";
		public DataGridViewRow Row { get; set; }
		public bool IsRoot { get; set; }
		public bool Edited { get; set; }
		public bool Revalidate { get; set; }
		public bool ProcessEditedManually { get; set; }
		public ProcessHoleFact[] PendingHoleFacts { get; set; } = Array.Empty<ProcessHoleFact>();
		public bool HoleGeometryPending { get; set; }
		public bool HoleOnlyIncrement { get; set; }
		public bool Deep { get; set; }
		public HashSet<string> ReferencedConfigurations { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public int? Stamp { get; set; }
		public string Configuration { get; set; }
		public Dictionary<string, string> PendingProperties { get; } = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
		public bool IsCached => CachedProcess.Length > 0;
	}

	private sealed class ManualWrite
	{
		public ExistingProcessEditTarget ProcessEdit;
		public bool PurchaseOnly;
		public bool PurchaseValue;
		public PropertyWriteBatch.Snapshot PurchaseSnapshot;
		public Dictionary<string, string> Values;
		public int? Stamp;
		public string Configuration, FileStamp;
		public BatchPart Part { get; set; }
		public DataGridViewRow Row { get; set; }
		public string Process { get; set; } = "";
	}
}
