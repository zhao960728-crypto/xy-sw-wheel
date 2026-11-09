using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal sealed class ProcessRecognitionWindow : PluginToolWindow
{
	private readonly Func<ISldWorks> _getSolidWorks;
	private readonly Action<ProcessRecognitionConfig> _saveConfig;
	private readonly Action _onPropertiesWritten;
	private ProcessRecognitionConfig _config;
	private readonly Func<ProcessRecognitionConfig> _loadConfig;
	private readonly Label _modelLabel = new Label { AutoSize = true, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) };
	private readonly Label _statusLabel = new Label { AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105) };
	private readonly DataGridView _grid = new DataGridView
	{
		Dock = DockStyle.Fill,
		AllowUserToAddRows = false,
		AllowUserToDeleteRows = false,
		AllowUserToResizeRows = false,
		RowHeadersVisible = false,
		AutoGenerateColumns = false,
		SelectionMode = DataGridViewSelectionMode.FullRowSelect,
		MultiSelect = false
	};
	private readonly TextBox _propertyName = new TextBox { Width = 130 };
	private readonly TextBox _separator = new TextBox { Width = 45 };
	private readonly TextBox _remarkValue = new TextBox { Width = 180 };
	private readonly ComboBox _combination = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDown };
	private readonly ComboBox _category = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Key" };
	private readonly Dictionary<string, string> _pendingAdditionalProperties = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
	private ProcessRecognitionInput _lastInput;
	private bool _lastDeep;
	private ProcessHoleFact[] _pendingHoleFacts = Array.Empty<ProcessHoleFact>();
	private bool _holeGeometryPending, _holeOnlyIncrement;
	private ExistingProcessEditTarget _existingProcessEdit;
	private bool? _pendingPurchase;
	private PropertyWriteBatch.Snapshot _purchaseSnapshot;
	private IModelDoc2 _recognizedDocument;
	private int _recognizedStamp;
	private string _recognizedConfiguration;
	private string _recognizedSettings;
	private bool _edited, _loading;
	private bool _sidebarPrepared;
	private readonly CheckBox _independentSteps = new CheckBox { Text = "逐道工序下拉编辑", AutoSize = true };
	internal Action SidebarCloseRequested;
	private readonly CheckBox _keepEdits = new CheckBox { Text = "保留手工调整", Checked = true, AutoSize = true };
	private readonly TextBox _preview = new TextBox { ReadOnly = true, Multiline = true, Height = 80, Dock = DockStyle.Bottom, ScrollBars = ScrollBars.Vertical };
	private readonly DocumentRecognitionSessions<Draft> _drafts = new DocumentRecognitionSessions<Draft>();
	internal int RecognitionReadCount { get; private set; }
	internal Func<ProcessRecognitionWorkload, string, bool> RecognitionConfirmationForTest { get; set; }
	private IModelDoc2 _backgroundFailureDocument;
	private int _backgroundFailureStamp;
	private string _backgroundFailureConfiguration, _backgroundFailureSettings;
	private ProcessRecognitionBatchCache _resultCache;
	private string _resultCacheSettings;
	internal string ResultCachePathForTest { get; set; }
	private sealed class Draft
	{
		public IModelDoc2 Document;
		public string Configuration, Settings, PropertyName, Separator;
		public int Stamp;
		public bool Edited;
		public bool Deep;
		public ProcessHoleFact[] PendingHoleFacts;
		public bool HoleGeometryPending, HoleOnlyIncrement;
		public bool? PendingPurchase;
		public PropertyWriteBatch.Snapshot PurchaseSnapshot;
		public ProcessRecognitionInput Input;
		public ExistingProcessEditTarget ExistingProcessEdit;
		public List<RecognizedProcess> Rows;
		public Dictionary<string, string> Pending;
	}
	private void RememberDraft()
	{
		if (_lastInput == null || _recognizedDocument == null) return;
		_grid.EndEdit();
		_drafts.Put(_recognizedDocument, _recognizedConfiguration, new Draft { Document = _recognizedDocument, Configuration = _recognizedConfiguration, Stamp = _recognizedStamp,
			Settings = _recognizedSettings, Input = _lastInput, Deep = _lastDeep, PendingHoleFacts = _pendingHoleFacts, HoleGeometryPending = _holeGeometryPending, HoleOnlyIncrement = _holeOnlyIncrement, ExistingProcessEdit = _existingProcessEdit, Rows = ReadRows(), Edited = _edited, PendingPurchase = _pendingPurchase, PurchaseSnapshot = _purchaseSnapshot,
			PropertyName = _propertyName.Text, Separator = _separator.Text, Pending = new Dictionary<string, string>(_pendingAdditionalProperties) });
	}
	private void DisplayRows(IEnumerable<RecognizedProcess> rows)
	{
		_loading = true;
		try
		{
			_grid.Rows.Clear();
			foreach (var row in rows)
			{
				int index = _grid.Rows.Add(_grid.Rows.Count + 1, row.Name, row.Confidence <= 0 ? "手动" : row.Confidence + "%", row.Evidence);
				if (row.Confidence > 0 && row.Confidence < 80) _grid.Rows[index].Cells["Confidence"].Style.ForeColor = Color.DarkOrange;
			}
		}
		finally { _loading = false; }
		UpdatePreview();
	}
	private void UpdatePreview() => _preview.Text = PurchasedPartPolicy.Display(_lastInput) + (_pendingPurchase.HasValue ? "；待确认：" + PurchasedPartPolicy.PropertyName + "=" + (_pendingPurchase.Value ? "是" : "否") + "（只写当前配置，不修改工序）" : "") + "\r\n待写入工序" + (_existingProcessEdit == null ? "" : "（修改原" + _existingProcessEdit.ScopeLabel + "）") + "：" + ProcessRecognitionService.Join(ReadRows().Select(row => row.Name), _separator.Text)
		+ (_pendingAdditionalProperties.Count > 0 ? "\r\n附加属性：" + string.Join("；", _pendingAdditionalProperties.Select(pair => pair.Key + "=" + pair.Value)) : "")
		+ (_pendingHoleFacts.Length > 0 ? "\r\n待合并确认孔工序：" + string.Join("、", _pendingHoleFacts.Where(f => f.IsConfirmed).Select(f => f.ProcessName).Distinct()) : "")
		+ (_holeGeometryPending ? "\r\n孔工艺已更新，几何待快速复核；原工序草稿保留。" : "");
	internal void ApplyHoleProcessFacts(ProcessHoleFactMessage message)
	{
		if (message?.Document == null || IsDisposed || message.Source == "ProcessRead") return;
		foreach (var draft in _drafts.Values.Where(d => Equals(d.Document, message.Document) && d.Configuration == message.Configuration))
		{
			draft.Input.HoleFacts = message.Facts;
			if (draft.Stamp != message.UpdateStamp) { ProcessHoleSuggestionPolicy.MarkGeometryPending(draft.Input.Geometry); draft.HoleGeometryPending = true; }
			if (!draft.Edited && draft.ExistingProcessEdit == null && !draft.HoleGeometryPending) draft.Rows = ProcessHoleSuggestionPolicy.MergeMissing(draft.Rows, message.Facts, _config).ToList();
			else draft.PendingHoleFacts = message.Facts;
		}
		if (_lastInput == null || !Equals(_recognizedDocument, message.Document) || _recognizedConfiguration != message.Configuration) return;
		_lastInput.HoleFacts = message.Facts;
		if (_recognizedStamp != message.UpdateStamp) { ProcessHoleSuggestionPolicy.MarkGeometryPending(_lastInput.Geometry); _holeGeometryPending = true; }
		if (!_edited && _existingProcessEdit == null && !_holeGeometryPending && !_grid.IsCurrentCellInEditMode) DisplayRows(ProcessHoleSuggestionPolicy.MergeMissing(ReadRows(), message.Facts, _config));
		else _pendingHoleFacts = message.Facts;
		UpdatePreview();
		_statusLabel.Text = _holeGeometryPending ? "孔工艺已更新，原工序保留、几何待复核；可合并确认孔工序，再快速识别复核后写入。" : "孔工艺事实已更新，已有工序和人工草稿保留；需要时点击合并孔工序建议。";
	}
	private void AcceptHoleProcessSuggestions()
	{
		if (!_grid.EndEdit()) return;
		if (_pendingHoleFacts.Length == 0) { _statusLabel.Text = "当前没有待合并的已核实孔工序。"; return; }
		var before = ReadRows(); var merged = ProcessHoleSuggestionPolicy.MergeMissing(before, _pendingHoleFacts, _config);
		_pendingHoleFacts = Array.Empty<ProcessHoleFact>();
		if (merged.Count == before.Count) { _statusLabel.Text = "已核实孔工序已在当前列表中，未重复添加。"; return; }
		_edited = true; _holeOnlyIncrement = true; DisplayRows(merged);
		_statusLabel.Text = "已合并缺少的确认孔工序，其他工序顺序保留；" + (_holeGeometryPending ? "请先快速识别复核当前模型，再确认写入。" : "确认写入后仅更新工序和明确暂存的附加属性。");
	}

    private SurfaceProcessLinkState _surfaceDraftLink = new SurfaceProcessLinkState();
    private IModelDoc2 _surfaceDraftDocument;
    private string _surfaceDraftConfiguration;
    internal void ApplySurfaceProcessLink(SurfaceProcessLinkService.Result update)
    {
        if (IsDisposed || update.Document == null || !Equals(update.Document, _recognizedDocument) || update.Configuration != _recognizedConfiguration) return;
        if (!string.IsNullOrEmpty(update.Status)) _statusLabel.Text = update.Status;
        if (_grid.IsCurrentCellInEditMode) return;
        if (update.Changed)
        {
            if (update.Source.PropertyName != _propertyName.Text) return;
            if (_existingProcessEdit != null && (_existingProcessEdit.Snapshot.Raw != update.PreviousSequence || _existingProcessEdit.Configuration != update.Source.Configuration)) return;
            var draftRows = ReadRows();
            string draft = string.Join(_separator.Text, draftRows.Select(row => row.Name));
            string merged = SurfaceProcessLinkPolicy.MergeDraft(draft, update.PreviousSequence, update.Sequence, _separator.Text);
            _existingProcessEdit = update.Source;
            if (_lastInput != null) { _lastInput.ExistingProcess = update.Sequence; _lastInput.SurfaceTreatment = update.Surface; }
            if (_edited)
            {
                var names = ProcessSequenceEditorWindow.Split(merged, _separator.Text);
                for (int i = 0; i < draftRows.Count && i < names.Length; i++) draftRows[i].Name = names[i];
                DisplayRows(draftRows);
                _statusLabel.Text = "表处属性已同步，其他人工工序草稿保留，可直接确认写入。";
            }
            else DisplayRows(update.Source.Rows(_separator.Text));
            _recognizedStamp = update.Document.GetUpdateStamp();
            return;
        }
        // An unwritten recognition preview also follows its one explicit slot.
        // Persisted/model source snapshots are handled above, never re-recognized.
        if (_existingProcessEdit != null || update.Status.Length > 0 || update.Surface.Length == 0) return;
        if (!Equals(_surfaceDraftDocument, update.Document) || _surfaceDraftConfiguration != update.Configuration)
        { _surfaceDraftDocument = update.Document; _surfaceDraftConfiguration = update.Configuration; _surfaceDraftLink = new SurfaceProcessLinkState(); }
        var rows = ReadRows();
        string before = string.Join(_separator.Text, rows.Select(row => row.Name));
        if (!SurfaceProcessLinkPolicy.Plan(before, update.Surface, _separator.Text, _surfaceDraftLink, out string sequence, out int token, out string reason))
        { if (reason.Length > 0) _statusLabel.Text = reason; return; }
        int index = token / 2;
        if (index >= rows.Count) return;
        rows[index].Name = update.Surface.Trim(); rows[index].Evidence = "来自表面处理属性，其他工序草稿保留";
        DisplayRows(rows); SurfaceProcessLinkPolicy.Commit(_surfaceDraftLink, sequence, update.Surface, token);
        _statusLabel.Text = "已按表面处理属性更新这一道工序预览；底部确认后写入模型。";
    }

	internal void ResumeRecognition() => ResumeRecognitionCore(background: false, allowRead: true);
	internal void ResumeRecognitionCore(bool background, bool allowRead = true)
	{
		if (background && (_grid.IsCurrentCellInEditMode || OwnedForms.OfType<ProcessRulesWindow>().Any())) return;
		_config = _loadConfig?.Invoke() ?? _config;
		if (background && BackgroundFailureUnchanged()) return;
		if (HasCurrentResult()) return;
		IModelDoc2 active = _getSolidWorks()?.ActiveDoc as IModelDoc2;
		if (HasManualDraft && Equals(active, _recognizedDocument) && active?.ConfigurationManager.ActiveConfiguration.Name == _recognizedConfiguration)
		{ _statusLabel.Text = "模型或设置已变化，人工草稿仍保留；请明确重新识别并检查后写入。"; return; }
		RememberDraft();
		RefreshCombinations();
		if (!HasManualDraft && _recognizedSettings != null && _recognizedSettings != ProcessRecognitionBatchCache.SettingsKey(_config))
		{
			_propertyName.Text = _config.SummaryPropertyName;
			_separator.Text = _config.Separator;
		}
		if (_drafts.TryGet(active, active?.ConfigurationManager.ActiveConfiguration.Name, out Draft saved))
		{
			_recognizedDocument = saved.Document; _recognizedConfiguration = saved.Configuration; _recognizedStamp = saved.Stamp;
			_recognizedSettings = saved.Settings; _lastInput = saved.Input; _existingProcessEdit = saved.ExistingProcessEdit; _edited = saved.Edited; _pendingPurchase = saved.PendingPurchase; _purchaseSnapshot = saved.PurchaseSnapshot;
			_lastDeep = saved.Deep;
			_pendingHoleFacts = saved.PendingHoleFacts ?? Array.Empty<ProcessHoleFact>(); _holeGeometryPending = saved.HoleGeometryPending; _holeOnlyIncrement = saved.HoleOnlyIncrement;
			_propertyName.Text = saved.PropertyName; _separator.Text = saved.Separator;
			_pendingAdditionalProperties.Clear(); foreach (var pair in saved.Pending) _pendingAdditionalProperties[pair.Key] = pair.Value;
			DisplayRows(saved.Rows);
			_modelLabel.Text = "当前零件：" + saved.Input.DocumentTitle + " / " + saved.Configuration;
			if (HasCurrentResult()) { _statusLabel.Text = "已复用本零件当前配置的工序结果，未重复扫描；尚未写入的调整仍保留。"; return; }
			if (HasManualDraft) { _statusLabel.Text = "模型或设置已变化，人工草稿仍保留；请明确重新识别并检查后写入。"; return; }
		}
		if (saved == null && TryRestoreSavedResult(active)) return;
		if (!allowRead)
		{
			_pendingHoleFacts = Array.Empty<ProcessHoleFact>(); _holeGeometryPending = _holeOnlyIncrement = false;
			_lastInput = null; _recognizedDocument = null; _existingProcessEdit = null; _pendingAdditionalProperties.Clear(); _pendingPurchase = null; _purchaseSnapshot = null; _edited = false;
			_loading = true; try { _grid.Rows.Clear(); } finally { _loading = false; }
			_modelLabel.Text = "当前零件：" + (active?.GetTitle() ?? "未读取"); _preview.Clear(); _statusLabel.Text = "等待当前零件稳定后后台识别；识别结果只暂存，不自动写入模型。"; return;
		}
		RecognizeCore(_config.DefaultMode == ProcessRecognitionModes.Deep, background);
	}
	private ProcessRecognitionBatchCache ResultCache()
	{
		string settings = ProcessRecognitionBatchCache.SettingsKey(_config);
		if (_resultCache == null || _resultCacheSettings != settings)
		{
			_resultCache?.Save(); _resultCache = ResultCachePathForTest == null ? new ProcessRecognitionBatchCache(_config) : new ProcessRecognitionBatchCache(_config, ResultCachePathForTest);
			_resultCacheSettings = settings;
		}
		return _resultCache;
	}
	private bool TryRestoreSavedResult(IModelDoc2 document)
	{
		if (document == null || document.GetSaveFlag()) return false;
		string configuration = document.ConfigurationManager.ActiveConfiguration.Name;
		if (!ResultCache().TryGetSnapshotInfo(document.GetPathName(), configuration, requireDeep: false, out ProcessRecognitionInput input, out IReadOnlyList<RecognizedProcess> processes, out bool snapshotDeep)) return false;
		_lastInput = input; _lastInput.DocumentTitle = document.GetTitle(); _lastDeep = snapshotDeep;
		_propertyName.Text = _config.SummaryPropertyName; _separator.Text = _config.Separator;
		SelectExistingPropertyName(document); _existingProcessEdit = ExistingProcessEditTarget.Effective(document, configuration, _propertyName.Text);
		_pendingAdditionalProperties.Clear(); _pendingPurchase = null; _purchaseSnapshot = null; _edited = false;
		_pendingHoleFacts = Array.Empty<ProcessHoleFact>(); _holeGeometryPending = _holeOnlyIncrement = false;
		DisplayRows(_existingProcessEdit == null ? processes : _existingProcessEdit.Rows(_separator.Text));
		CaptureContext(document); _modelLabel.Text = "当前零件：" + input.DocumentTitle + " / " + configuration;
		_statusLabel.Text = "已复用保存文件的暂存识别结果，文件、配置和判断规则一致，未重复扫描几何；写入前仍核验当前模型。";
		if (!snapshotDeep) _statusLabel.Text += " 暂存为轻量结果，需要几何分析时可点击深度识别。";
		return true;
	}
	private bool HasManualDraft => _edited || _pendingPurchase.HasValue || _pendingAdditionalProperties.Count > 0 || _pendingHoleFacts.Length > 0 || _holeGeometryPending;
	private bool BackgroundFailureUnchanged()
	{
		if (_backgroundFailureDocument == null) return false;
		try { var current = CurrentDocument(); return Equals(current, _backgroundFailureDocument) && current.GetUpdateStamp() == _backgroundFailureStamp && current.ConfigurationManager.ActiveConfiguration.Name == _backgroundFailureConfiguration && ProcessRecognitionBatchCache.SettingsKey(_config) == _backgroundFailureSettings; }
		catch { return false; }
	}
	internal void RetainOpenDocuments(IEnumerable<object> documents)
	{
		var open = (documents ?? Array.Empty<object>()).ToArray(); _drafts.RetainOpenDocuments(open);
		if (_backgroundFailureDocument != null && !open.Any(document => Equals(document, _backgroundFailureDocument))) _backgroundFailureDocument = null;
		if (_surfaceDraftDocument != null && !open.Any(document => Equals(document, _surfaceDraftDocument))) { _surfaceDraftDocument = null; _surfaceDraftConfiguration = null; _surfaceDraftLink = new SurfaceProcessLinkState(); }
		if (_recognizedDocument == null || open.Any(document => Equals(document, _recognizedDocument))) return;
		_pendingHoleFacts = Array.Empty<ProcessHoleFact>(); _holeGeometryPending = _holeOnlyIncrement = false;
		_lastInput = null; _recognizedDocument = null; _existingProcessEdit = null; _pendingAdditionalProperties.Clear(); _pendingPurchase = null; _purchaseSnapshot = null; _edited = false;
		_loading = true; try { _grid.Rows.Clear(); } finally { _loading = false; }
		_preview.Clear(); _statusLabel.Text = "当前模型已关闭，临时识别结果已释放。";
	}

	private bool HasCurrentResult()
	{
		try
		{
			IModelDoc2 document = CurrentDocument();
			return _lastInput != null && Equals(document, _recognizedDocument)
				&& document.GetUpdateStamp() == _recognizedStamp
				&& document.ConfigurationManager.ActiveConfiguration.Name == _recognizedConfiguration
				&& ProcessRecognitionBatchCache.SettingsKey(_config) == _recognizedSettings;
		}
		catch { return false; }
	}

	private void CaptureContext(IModelDoc2 document)
	{
		_recognizedDocument = document;
		_recognizedStamp = document.GetUpdateStamp();
		_recognizedConfiguration = document.ConfigurationManager.ActiveConfiguration.Name;
		_recognizedSettings = ProcessRecognitionBatchCache.SettingsKey(_config);
	}

	internal int PendingAdditionalPropertyCount => _pendingAdditionalProperties.Count;
	internal void StageAdditionalPropertyForTest(string propertyName, string value) => StageAdditionalProperty(propertyName, value);

	public ProcessRecognitionWindow(
		Func<ISldWorks> getSolidWorks,
		Func<ProcessRecognitionConfig> loadConfig = null,
		Action<ProcessRecognitionConfig> saveConfig = null,
		Action onPropertiesWritten = null)
	{
		_getSolidWorks = getSolidWorks ?? throw new ArgumentNullException(nameof(getSolidWorks));
		_saveConfig = saveConfig;
		_loadConfig = loadConfig;
		_onPropertiesWritten = onPropertiesWritten;
		_config = loadConfig?.Invoke() ?? new ProcessRecognitionConfig();
		ProcessRecognitionConfig.Normalize(_config);
		_propertyName.Text = _config.SummaryPropertyName;
		_separator.Text = _config.Separator;
		Text = "工序识别";
		Size = new Size(850, 700);
		MinimumSize = new Size(760, 700);
		StartPosition = FormStartPosition.CenterParent;
		Font = new Font("Microsoft YaHei UI", 9f);
		BuildGrid();
		Controls.Add(BuildLayout());
		SizeChanged += (_, __) => ConstrainLabels(this);
		ConstrainLabels(this);
		_grid.CellEndEdit += (_, e) => { if (_loading || e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Process") return; _edited = true; _grid.Rows[e.RowIndex].Cells["Confidence"].Value = "手动"; _grid.Rows[e.RowIndex].Cells["Evidence"].Value = "人工调整，请按实际工艺确认"; UpdatePreview(); };
		_separator.TextChanged += (_, __) => UpdatePreview();
		Shown += (_, __) => { if (TopLevel) ResumeRecognition(); };
		_independentSteps.CheckedChanged += (_, __) => ApplyProcessEditingMode();
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (e.CloseReason == CloseReason.UserClosing)
		{
			_grid.EndEdit();
			e.Cancel = true;
			Hide();
			SidebarCloseRequested?.Invoke();
		}
		base.OnFormClosing(e);
	}

	internal void PrepareForSidebar()
	{
		if (_sidebarPrepared) return;
		_sidebarPrepared = true;
		SuspendLayout();
		try
		{
			Control[] previous = Controls.Cast<Control>().ToArray();
			var buttons = ProcessSidebarLayout.Descendants(this).OfType<Button>().ToDictionary(button => button.Text);
			Button Find(string text) => buttons[text];
			var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), RowCount = 5, ColumnCount = 1, Margin = Padding.Empty };
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 174));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
			var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
			header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			header.Controls.Add(_modelLabel); header.Controls.Add(_statusLabel);
			root.Controls.Add(header, 0, 0);
			var tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 5) };
			ProcessSidebarLayout.Tab(tabs, "识别/编辑", Find("快速识别"), Find("深度识别"), Find("工序查看/分析"), Find("合并孔工序建议"), _keepEdits, _independentSteps,
				Find("选中后插入工序"), Find("删除选中"), Find("上移"), Find("下移"), Find("读取已有工序"), Find("预览写入差异"),
				ProcessSidebarLayout.Label("双击编辑；按住工序行拖动换序。橙色可信度建议人工复核。"));
			ProcessSidebarLayout.Tab(tabs, "工序组/属性", ProcessSidebarLayout.Label("工序组"), _combination, Find("应用工序组"), Find("存为工序组"),
				ProcessSidebarLayout.Label("类别"), _category, Find("应用类别"), Find("工序组/类型设置"),
				ProcessSidebarLayout.Label("备注值"), _remarkValue, Find("备注待写入"),
				ProcessSidebarLayout.Label("汇总属性"), _propertyName, ProcessSidebarLayout.Label("分隔符"), _separator);
			ProcessSidebarLayout.Tab(tabs, "外购", Find("标记外购"), Find("取消外购（设为否）"), Find("确认外购标记"),
				ProcessSidebarLayout.Label("只写当前配置的外购身份；保留工序和其他配置。"));
			root.Controls.Add(tabs, 0, 1);
			_grid.Columns["Order"].Width = 42;
			_grid.Columns["Process"].Width = 150;
			_grid.Columns["Confidence"].Width = 62;
			_grid.Columns["Evidence"].MinimumWidth = 120;
			_grid.ScrollBars = ScrollBars.Both;
			root.Controls.Add(_grid, 0, 2);
			_preview.Dock = DockStyle.Fill; root.Controls.Add(_preview, 0, 3);
			var actions = ProcessSidebarLayout.Flow(Find("确定写入"), Find("关闭"));
			actions.FlowDirection = FlowDirection.RightToLeft;
			root.Controls.Add(actions, 0, 4);
			Controls.Add(root);
            ProcessSidebarLayout.AutoHeightTabs(root, 1, tabs);
			foreach (Control old in previous) { Controls.Remove(old); old.Dispose(); }
			MinimumSize = new Size(280, 360);
			FormBorderStyle = FormBorderStyle.None;
			ProcessSidebarLayout.Wrap(this, _modelLabel, _statusLabel);
		}
		finally { ResumeLayout(true); }
	}

	private void ApplyProcessEditingMode()
	{
		if (_grid.Columns["Process"] == null || !_grid.EndEdit()) return;
		int currentRow = _grid.CurrentCell?.RowIndex ?? -1;
		int currentColumn = _grid.CurrentCell?.ColumnIndex ?? -1;
		bool wasLoading = _loading;
		_loading = true;
		try
		{
			string[] choices = ProcessSidebarLayout.Choices(_config).Concat(_grid.Rows.Cast<DataGridViewRow>().Select(row => Convert.ToString(row.Cells["Process"].Value)))
				.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
			DataGridViewCell template = _independentSteps.Checked ? (DataGridViewCell)new ProcessNameComboCell { Candidates = choices } : new DataGridViewTextBoxCell();
			var column = _grid.Columns["Process"];
			column.CellTemplate = template;
			column.HeaderText = _independentSteps.Checked ? "工序（下拉/输入）" : "工序（双击编辑）";
			foreach (DataGridViewRow row in _grid.Rows)
			{
				DataGridViewCell old = row.Cells[column.Index];
				var replacement = (DataGridViewCell)template.Clone();
				replacement.Value = old.Value; replacement.Style = old.Style.Clone(); replacement.ToolTipText = old.ToolTipText;
				row.Cells[column.Index] = replacement;
			}
			if (currentRow >= 0 && currentColumn >= 0) _grid.CurrentCell = _grid.Rows[currentRow].Cells[currentColumn];
		}
		finally { _loading = wasLoading; }
	}

	private Control BuildLayout()
	{
		TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 6, ColumnCount = 1 };
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

		TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, 10) };
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		header.Controls.Add(_modelLabel, 0, 0);
		header.Controls.Add(_statusLabel, 0, 1);
		layout.Controls.Add(header, 0, 0);

		FlowLayoutPanel recognition = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
		recognition.Controls.Add(Button("快速识别", () => Recognize(deep: false), primary: _config.DefaultMode == ProcessRecognitionModes.Quick));
		recognition.Controls.Add(Button("深度识别", () => Recognize(deep: true), primary: _config.DefaultMode == ProcessRecognitionModes.Deep));
		recognition.Controls.Add(Button("工序查看/分析", ShowAnalysis));
		recognition.Controls.Add(Button("合并孔工序建议", AcceptHoleProcessSuggestions));
		recognition.Controls.Add(_keepEdits);
		recognition.Controls.Add(_independentSteps);
		recognition.Controls.Add(new Label
		{
			AutoSize = true,
			Text = "快速读取特征和已有属性；导入件或无特征模型请用深度识别。橙色可信度表示建议人工复核。",
			ForeColor = Color.FromArgb(100, 116, 139),
			Margin = new Padding(12, 7, 0, 0)
		});
		layout.Controls.Add(recognition, 0, 1);
		var results = new Panel { Dock = DockStyle.Fill };
		results.Controls.Add(_grid); results.Controls.Add(_preview);
		layout.Controls.Add(results, 0, 2);

		FlowLayoutPanel editing = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
		editing.Controls.Add(Button("选中后插入工序", AddRow));
		editing.Controls.Add(Button("删除选中", RemoveSelected));
		editing.Controls.Add(Button("上移", () => MoveSelected(-1)));
		editing.Controls.Add(Button("下移", () => MoveSelected(1)));
		editing.Controls.Add(new Label { Text = "按住工序行拖动换序", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
		editing.Controls.Add(Button("读取已有工序", ReadExistingProcess));
		editing.Controls.Add(Button("标记外购", () => StagePurchaseMarker(true)));
		editing.Controls.Add(Button("取消外购（设为否）", () => StagePurchaseMarker(false)));
		editing.Controls.Add(Button("确认外购标记", ConfirmPurchaseMarker));
		editing.Controls.Add(Button("预览写入差异", PreviewChanges));
		editing.Controls.Add(_combination);
		editing.Controls.Add(Button("应用工序组", ApplyCombination));
		editing.Controls.Add(Button("存为工序组", SaveCombination));
		RefreshCombinations();
		layout.Controls.Add(editing, 0, 3);

		FlowLayoutPanel attributes = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 8, 0, 0) };
		attributes.Controls.Add(new Label { AutoSize = true, Text = "待写入属性", Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 7, 10, 0) });
		attributes.Controls.Add(_category);
		attributes.Controls.Add(Button("应用类别", () => { if (_category.SelectedItem is KeyValuePair<string, string> pair) { var rows = ProcessRecognitionConfig.ResolveCombination(_config, pair.Value); if (rows.Count > 0) { _grid.EndEdit(); _edited = true; DisplayRows(rows); } StageAdditionalProperty(_config.ProcessTypePropertyName, pair.Value); } }));
		attributes.Controls.Add(Button("工序组/类型设置", EditProcessRules));
		attributes.Controls.Add(new Label { AutoSize = true, Text = "备注值", Margin = new Padding(8, 7, 4, 0) });
		attributes.Controls.Add(_remarkValue);
		attributes.Controls.Add(Button("备注待写入", () => StageAdditionalProperty(_config.RemarkPropertyName, _remarkValue.Text)));
		layout.Controls.Add(attributes, 0, 4);

		TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 10, 0, 0) };
		footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		FlowLayoutPanel property = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
		property.Controls.Add(new Label { AutoSize = true, Text = "汇总属性", Margin = new Padding(0, 7, 6, 0) });
		property.Controls.Add(_propertyName);
		property.Controls.Add(new Label { AutoSize = true, Text = "分隔符", Margin = new Padding(12, 7, 6, 0) });
		property.Controls.Add(_separator);
		footer.Controls.Add(property, 0, 0);
		FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
		Button confirmWrite = Button("确定写入", WriteProperty, primary: true);
		actions.Controls.Add(confirmWrite);
		actions.Controls.Add(Button("关闭", Close));
		AcceptButton = confirmWrite;
		footer.Controls.Add(actions, 1, 0);
		layout.Controls.Add(footer, 0, 5);
		return layout;
	}

	private void StageAdditionalProperty(string propertyName, string value)
	{
		string name = (propertyName ?? "").Trim();
		string text = (value ?? "").Trim();
		if (name.Length == 0 || text.Length == 0)
		{
			_statusLabel.Text = name.Length == 0 ? "属性名为空，请先在主设置中配置。" : "请先填写属性值。";
			return;
		}
		_pendingAdditionalProperties[name] = text;
		UpdatePreview();
		_statusLabel.Text = "待写入：" + name + " = " + text + "；模型尚未修改，请点击“确定写入”。";
	}

	private void BuildGrid()
	{
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Order", HeaderText = "序号", ReadOnly = true, Width = 55 });
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Process", HeaderText = "工序（双击编辑）", Width = 150 });
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Confidence", HeaderText = "可信度", ReadOnly = true, Width = 75 });
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Evidence", HeaderText = "识别依据 / 工艺说明", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
		ProcessSequenceGrid.Attach(_grid, () => { _edited = true; Renumber(); _statusLabel.Text = "工序顺序已调整，模型尚未修改。"; });
	}

	private Button Button(string text, Action action, bool primary = false)
	{
		Button button = new Button
		{
			Text = text,
			AutoSize = true,
			Height = 31,
			FlatStyle = FlatStyle.Flat,
			BackColor = primary ? Color.FromArgb(30, 136, 229) : Color.White,
			ForeColor = primary ? Color.White : Color.FromArgb(35, 45, 55),
			Margin = new Padding(0, 0, 8, 0)
		};
		button.FlatAppearance.BorderColor = primary ? button.BackColor : Color.FromArgb(210, 216, 222);
		button.Click += (_, __) => action();
		return button;
	}

	private void Recognize(bool deep) => RecognizeCore(deep, background: false);
	private void RecognizeCore(bool deep, bool background)
	{
		try
		{
			IModelDoc2 document = CurrentDocument();
			bool automaticFallback = false;
			if (deep)
			{
				var workload = ProcessRecognitionWorkload.Read(document);
				if (background && !workload.AllowAutomaticDeep) { deep = false; automaticFallback = true; }
				else if (!background && !workload.Confirm(this, "当前零件深度识别", RecognitionConfirmationForTest)) { _statusLabel.Text = "已取消深度识别，原工序结果和人工草稿保留。"; return; }
			}
			_backgroundFailureDocument = null;
			RememberDraft();
			bool sameContext = Equals(document, _recognizedDocument) && document.ConfigurationManager.ActiveConfiguration.Name == _recognizedConfiguration;
			if (!sameContext) { _propertyName.Text = _config.SummaryPropertyName; _separator.Text = _config.Separator; }
			List<RecognizedProcess> manual = _edited && _keepEdits.Checked && sameContext ? ReadRows() : null;
			RecognitionReadCount++;
			ProcessRecognitionInput input = SolidWorksProcessFeatureReader.Read(document, deep, _config, maxGeometryFaces: background ? 600 : 1200);
			if (!input.IsPart)
			{
				throw new InvalidOperationException("请先打开零件文档。装配体和工程图不会被修改。");
			}
			_modelLabel.Text = "当前零件：" + (string.IsNullOrWhiteSpace(input.DocumentTitle) ? "未命名零件" : input.DocumentTitle) + " / " + document.ConfigurationManager.ActiveConfiguration.Name;
			_statusLabel.Text = (deep ? "深度识别" : "快速识别") + "完成，共读取 " + input.Features.Count + " 个特征"
				+ GeometryStatus(input.Geometry)
				+ (string.IsNullOrWhiteSpace(input.Material) ? "" : "；材料：" + input.Material)
				+ (string.IsNullOrWhiteSpace(input.SurfaceTreatment) ? "" : "；表面处理属性：" + input.SurfaceTreatment);
			if (automaticFallback) _statusLabel.Text += "；" + ProcessRecognitionWorkload.AutomaticFallbackText;
			_lastInput = input;
			_lastDeep = deep;
			_holeGeometryPending = false; _pendingHoleFacts = Array.Empty<ProcessHoleFact>();
			if (!sameContext || manual == null) _holeOnlyIncrement = false;
			if (!sameContext) { _pendingAdditionalProperties.Clear(); _pendingPurchase = null; _purchaseSnapshot = null; }
			SelectExistingPropertyName(document);
			if (manual == null) _existingProcessEdit = ExistingProcessEditTarget.Effective(document, document.ConfigurationManager.ActiveConfiguration.Name, _propertyName.Text);
			DisplayRows(manual ?? (_existingProcessEdit != null ? _existingProcessEdit.Rows(_separator.Text).ToList() : ProcessRecognitionService.Recognize(input, deep, _config).ToList()));
			_edited = manual != null;
			if (manual != null) _statusLabel.Text += "；手工工序和顺序已保留，取消勾选后可重新生成。";
			SelectExistingPropertyName(document);
			CaptureContext(document);
			if (!document.GetSaveFlag() && manual == null)
			{
				try { var cache = ResultCache(); cache.PutSnapshot(document.GetPathName(), _recognizedConfiguration, AssemblyProcessRecognitionWindow.Classification(input, ReadRows()), input, ReadRows(), deep); cache.Save(); }
				catch (Exception cacheError) { Logger.Trace("Part recognition snapshot not saved: " + cacheError.Message); _statusLabel.Text += "；识别结果已保留，文件暂存未完成"; }
			}
		}
		catch (Exception ex)
		{
			Logger.Error("Process recognition failed", ex);
			if (background)
			{
				try { var failed = CurrentDocument(); _backgroundFailureDocument = failed; _backgroundFailureStamp = failed.GetUpdateStamp(); _backgroundFailureConfiguration = failed.ConfigurationManager.ActiveConfiguration.Name; _backgroundFailureSettings = ProcessRecognitionBatchCache.SettingsKey(_config); } catch { _backgroundFailureDocument = null; }
				_statusLabel.Text = "后台识别暂未完成：" + ex.Message + "；可使用识别按钮重新尝试。";
			}
			else ToolNotice.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
	}

	private static string GeometryStatus(ProcessGeometryFact geometry)
	{
		if (geometry == null || geometry.SolidBodyCount <= 0) return "；未读取到实体几何";
		if (!geometry.GeometryComplete) return "；" + geometry.SolidBodyCount + " 个实体，" + (geometry.FaceCount == 0 ? "几何尚未读取，请深度识别" : "仅部分几何可分析，需复核未读取/未知曲面");
		if (geometry.StraightTubeBodyCount > 0 && !geometry.IsSheetLike) return "；" + geometry.SolidBodyCount + " 个实体，直管 " + geometry.StraightTubeBodyCount + " 个" + (geometry.ProfileKind.Length > 0 ? "（" + geometry.ProfileKind + "）" : "");
		if (!geometry.IsSheetLike) return "；实体几何：" + geometry.SolidBodyCount + " 个实体、" + geometry.FaceCount + " 个面";
		return "；实体几何：等厚薄板 " + geometry.ThicknessMm.ToString("0.###") + "mm、折弯 " + geometry.BendCount + " 处、内轮廓 " + geometry.CutoutCount + " 个" + (geometry.CutoutCountReliable ? "" : "（总数未确认，不写入；" + geometry.CutoutCountEvidence + "）");
	}

	private IModelDoc2 CurrentDocument()
	{
		return _getSolidWorks()?.ActiveDoc as IModelDoc2 ?? throw new InvalidOperationException("当前没有打开模型。");
	}

	private void SelectExistingPropertyName(IModelDoc2 document)
	{
		string existing = PropertyIo.Names(document).FirstOrDefault(name =>
			name.Equals(_config.SummaryPropertyName, StringComparison.CurrentCultureIgnoreCase)
			|| name.Equals("工序", StringComparison.CurrentCultureIgnoreCase)
			|| name.Equals("工艺流程", StringComparison.CurrentCultureIgnoreCase)
			|| name.Equals("工艺", StringComparison.CurrentCultureIgnoreCase));
		if (!string.IsNullOrWhiteSpace(existing)) _propertyName.Text = existing;
	}

	private void AddRow()
	{
		_edited = true;
		int index = ProcessSequenceGrid.InsertAfterSelection(_grid, 0, "", "手动", "手动添加");
		if (index < 0) return;
		Renumber();
		_grid.CurrentCell = _grid.Rows[index].Cells["Process"];
		_grid.BeginEdit(true);
	}

	private void RemoveSelected()
	{
		if (_grid.CurrentRow == null || !_grid.EndEdit()) return;
		_grid.Rows.Remove(_grid.CurrentRow);
		_edited = true;
		Renumber();
	}

	private void MoveSelected(int delta)
    {
        if (_grid.CurrentRow == null) return;
        int source = _grid.CurrentRow.Index;
        if (!ProcessSequenceGrid.Move(_grid, source, source + delta)) return;
        _edited = true;
        Renumber();
    }

	private void Renumber()
	{
		for (int index = 0; index < _grid.Rows.Count; index++) _grid.Rows[index].Cells["Order"].Value = index + 1;
		UpdatePreview();
	}
	private void RefreshCombinations()
	{
		string selected = _combination.Text;
		_combination.Items.Clear();
		_combination.Items.AddRange(ProcessRecognitionConfig.ParseCombinations(_config.ProcessCombinations).Select(pair => (object)pair.Key).ToArray());
		_combination.Text = selected;
		string category = _category.Text; _category.Items.Clear();
		_category.Items.AddRange(ProcessRecognitionConfig.ParseProcessNameMappings(_config.ProcessCategories).Select(pair => (object)pair).ToArray());
		if (_category.Items.Count > 0) _category.SelectedIndex = 0;
		if (category.Length > 0) _category.Text = category;
		if (_independentSteps.Checked) ApplyProcessEditingMode();
	}
	private void EditProcessRules()
	{
		if (OwnedForms.OfType<ProcessRulesWindow>().Any()) { OwnedForms.OfType<ProcessRulesWindow>().First().Activate(); return; }
		var dialog = new ProcessRulesWindow(_loadConfig?.Invoke() ?? _config);
		dialog.ShowModeless(this, result => { if (result != DialogResult.OK) return;
			try { if (_saveConfig == null) throw new InvalidOperationException("当前窗口不支持保存设置。");
				var latest = ProcessRulesWindow.Copy(_loadConfig?.Invoke() ?? _config); ProcessRulesWindow.CopyRules(dialog.Result, latest);
				_saveConfig(latest); _config = latest; RefreshCombinations(); _statusLabel.Text = "目录、工序组与判断规则已保存；重新识别立即使用新规则，已有工序和手工草稿保留。";
			} catch (Exception ex) { _statusLabel.Text = ex.Message; Logger.Error("Save process rules failed", ex); }
		});
	}
	private void ShowAnalysis()
	{
		if (!_grid.EndEdit()) return;
		var open = OwnedForms.OfType<ProcessAnalysisWindow>().FirstOrDefault(); if (open != null) { open.Activate(); return; }
		new ProcessAnalysisWindow(_loadConfig?.Invoke() ?? _config, _lastInput, _lastDeep, ReadRows()).ShowModeless(this);
	}
	private void ApplyCombination()
	{
		var rows = ProcessRecognitionConfig.ResolveCombination(_config, _combination.Text);
		if (rows.Count == 0) { _statusLabel.Text = "请选择已保存的工序组；也可编辑列表后填写工序组名，点击“存为工序组”。"; return; }
		_grid.EndEdit(); _edited = true; DisplayRows(rows);
		StageAdditionalProperty(_config.ProcessTypePropertyName, _combination.Text);
	}
	private void SaveCombination()
	{
		_grid.EndEdit(); string name = _combination.Text.Trim(); var rows = ReadRows();
		if (name.Length == 0 || name.Length > 64 || name.IndexOfAny(new[] { '=', '|', '\r', '\n' }) >= 0 || rows.Count == 0)
		{ _statusLabel.Text = "请填写 1～64 字的工序组名（不含 = 或 |），并在列表中填写工序。"; return; }
		if (rows.Any(row => row.Name.IndexOfAny(new[] { '>', '→', '|', '\r', '\n' }) >= 0))
		{ _statusLabel.Text = "工序组中请每行填写一道工序，工序名不含 >、→ 或 |。"; return; }
		try
		{
			if (_saveConfig == null) throw new InvalidOperationException("当前窗口不支持保存设置。");
			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			var latest = _loadConfig?.Invoke() ?? _config;
			var updated = serializer.Deserialize<ProcessRecognitionConfig>(serializer.Serialize(latest));
			var combinations = ProcessRecognitionConfig.ParseCombinations(updated.ProcessCombinations).Where(pair => !string.Equals(pair.Key, name, StringComparison.CurrentCultureIgnoreCase)).ToList();
			if (combinations.Count >= 50 || rows.Count > 30) throw new InvalidOperationException("最多保存 50 个工序组，每个工序组最多 30 道工序。");
			combinations.Add(new KeyValuePair<string, string>(name, string.Join(">", rows.Select(row => row.Name))));
			updated.ProcessCombinations = string.Join("|", combinations.Select(pair => pair.Key + "=" + pair.Value));
			_saveConfig(updated); _config = updated; _edited = true; RefreshCombinations();
			_statusLabel.Text = "工序组已保存，可在设置页修改；当前工序草稿保留，模型尚未写入。";
		}
		catch (Exception ex) { _statusLabel.Text = ex.Message; Logger.Error("Save process combination failed", ex); }
	}
	private void ConstrainLabels(Control parent)
	{
		foreach (Control child in parent.Controls)
		{
			if (child is Label label && label.AutoSize) label.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 70), 0);
			ConstrainLabels(child);
		}
	}

	private void ReadExistingProcess()
	{
		try
		{
			if (!HasCurrentResult()) throw new InvalidOperationException("请先识别当前零件，旧草稿仍保留。");
			var source = ExistingProcessEditTarget.Effective(CurrentDocument(), _recognizedConfiguration, _propertyName.Text);
			string value = source?.Snapshot.Resolved;
			if (string.IsNullOrWhiteSpace(value)) { _statusLabel.Text = "当前模型尚未填写该工序属性，草稿未改变。"; return; }
			_existingProcessEdit = source;
			DisplayRows(source.Rows(_separator.Text));
			_edited = true; _statusLabel.Text = "已有工序已读入草稿，模型尚未修改。";
		}
		catch (Exception ex) { _statusLabel.Text = ex.Message; }
	}
	private void PreviewChanges()
	{
		try
		{
			_grid.EndEdit();
			if (!HasCurrentResult()) throw new InvalidOperationException("模型或设置已变化，请先重新识别；草稿仍保留。");
			var values = _existingProcessEdit == null ? ProcessRecognitionService.BuildPropertyValues(ReadRows(), _lastInput.Geometry, _config, _propertyName.Text, _separator.Text, _lastInput).ToList()
                : new List<KeyValuePair<string,string>> { new KeyValuePair<string,string>(_existingProcessEdit.PropertyName, ProcessRecognitionService.Join(ReadRows().Select(row => row.Name), _separator.Text)) };
			if (_holeOnlyIncrement) values = new List<KeyValuePair<string,string>> { new KeyValuePair<string,string>(_existingProcessEdit?.PropertyName ?? _propertyName.Text, ProcessRecognitionService.Join(ReadRows().Select(row => row.Name), _separator.Text)) };
			foreach (var pair in _pendingAdditionalProperties) { values.RemoveAll(value => value.Key.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)); values.Add(pair); }
			var store = new PropertyWriteBatch.ModelStore(CurrentDocument(), _existingProcessEdit?.Configuration ?? "");
			string diff = string.Join("\r\n\r\n", values.Select(pair => { var old = store.Read(pair.Key); return pair.Key + (old.Exists && old.Raw == pair.Value ? "（未变化）" : "（待更新）") + "\r\n原值：" + (old.Exists ? old.Raw : "未填写") + "\r\n新值：" + pair.Value; }));
			ToolNotice.Show(this, "写入范围：" + (_existingProcessEdit?.ScopeLabel ?? "当前文件的文档自定义属性") + "。\r\n模型尚未修改。\r\n\r\n" + diff, "工序写入差异");
		}
		catch (Exception ex) { _statusLabel.Text = ex.Message; }
	}

    private void StagePurchaseMarker(bool purchased)
    {
        try
        {
            if (!HasCurrentResult()) throw new InvalidOperationException("请先识别当前零件，模型或配置变化后需重新读取。");
            var document = CurrentDocument();
            _purchaseSnapshot = PurchasedPartPolicy.ReadSnapshot(document, _recognizedConfiguration);
            _pendingPurchase = purchased;
            UpdatePreview();
            _statusLabel.Text = "当前配置“" + _recognizedConfiguration + "”的外购标记：" + (_purchaseSnapshot.Exists ? _purchaseSnapshot.Raw : "未填写") + " → " + (purchased ? "是" : "否") + "。请检查后点击“确认外购标记”；现有工序和类型保持不变。";
        }
        catch (Exception ex) { _statusLabel.Text = ex.Message; }
    }

    private void ConfirmPurchaseMarker()
    {
        try
        {
            if (!_pendingPurchase.HasValue) throw new InvalidOperationException("请先选择标记或取消外购，检查预览后再确认。");
            if (!HasCurrentResult()) throw new InvalidOperationException("零件、配置或模型已变化，请重新识别并确认外购标记。");
            var document = CurrentDocument();
            if (document.GetType() != 1 || document.IsOpenedReadOnly() || document.GetActiveSketch2() != null) throw new InvalidOperationException("请在可写零件中退出草图后再确认。");
            bool purchased = _pendingPurchase.Value;
            int changed = PurchasedPartPolicy.Apply(new PropertyWriteBatch.ModelStore(document, _recognizedConfiguration), _purchaseSnapshot, purchased);
            _lastInput.Purchase = purchased ? "是" : "否";
            _lastInput.PurchaseMarkerSnapshot = PurchasedPartPolicy.ReadSnapshot(document, _recognizedConfiguration);
            _pendingPurchase = null; _purchaseSnapshot = null;
            CaptureContext(document); UpdatePreview(); _onPropertiesWritten?.Invoke();
            _statusLabel.Text = "已回读确认当前配置外购标记=" + _lastInput.Purchase + "，更新 " + changed + " 项；工序/类型和其他草稿保持不变，请保存模型。";
        }
        catch (Exception ex) { _statusLabel.Text = "外购标记未完成：" + ex.Message; }
    }

	private void WriteProperty()
	{
		try
		{
			if (!_grid.EndEdit()) throw new InvalidOperationException("请先完成当前工序的编辑。");
			IModelDoc2 document = CurrentDocument();
			if (document.GetType() != 1) throw new InvalidOperationException("当前文档不是零件，未写入任何属性。");
			if (!HasCurrentResult()) throw new InvalidOperationException("零件、配置或模型已变化，请重新识别后再写入。");
			if (document.IsOpenedReadOnly() || document.GetActiveSketch2() != null) throw new InvalidOperationException("请退出草图编辑，并确认零件可写后再操作。");
			string propertyName = (_propertyName.Text ?? "").Trim();
			if (propertyName.Length == 0) throw new InvalidOperationException("请输入要写入的属性名称。");
			List<RecognizedProcess> rows = ReadRows();
			string summary = ProcessRecognitionService.Join(rows.Select(row => row.Name), _separator.Text);
            if (_existingProcessEdit != null)
            {
                var source = _existingProcessEdit;
                if (!string.Equals(propertyName, source.PropertyName, StringComparison.Ordinal)) throw new InvalidOperationException("正在修改模型已有工序，请保留原属性名后确认。");
                int modified = source.Apply(new PropertyWriteBatch.ModelStore(document, source.Configuration), summary, _pendingAdditionalProperties);
                _pendingAdditionalProperties.Clear();
                _existingProcessEdit = ExistingProcessEditTarget.Read(document, source.Configuration, source.PropertyName);
                _lastInput.ExistingProcess = summary;
                _edited = false; CaptureContext(document); UpdatePreview(); _onPropertiesWritten?.Invoke();
                _statusLabel.Text = "已修改并回读" + source.ScopeLabel + "中的“" + source.PropertyName + "”，更新 " + modified + " 项；只写工序及本次明确暂存的附加属性，请保存模型。";
                return;
            }

			if (summary.Length == 0 && _pendingAdditionalProperties.Count == 0) throw new InvalidOperationException("当前没有可写入的内容，请先识别、手动填写或选择待写入属性。");

			List<KeyValuePair<string, string>> values = summary.Length == 0
				? new List<KeyValuePair<string, string>>()
				: ProcessRecognitionService.BuildPropertyValues(
					rows,
					_lastInput?.Geometry,
					_config,
					propertyName,
					_separator.Text, _lastInput).ToList();
			if (_holeOnlyIncrement && summary.Length > 0) values = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(propertyName, summary) };
			foreach (KeyValuePair<string, string> pending in _pendingAdditionalProperties)
			{
				values.RemoveAll(value => value.Key.Equals(pending.Key, StringComparison.CurrentCultureIgnoreCase));
				values.Add(pending);
			}
			int changed = PropertyWriteBatch.Apply(new PropertyWriteBatch.ModelStore(document), values);
			_existingProcessEdit = ExistingProcessEditTarget.Read(document, "", propertyName);
			_lastInput.ExistingProcess = summary;
			if (changed > 0) document.ForceRebuild3(false);

			_config.SummaryPropertyName = propertyName;
			_config.Separator = (_separator.Text ?? "").Trim();
			ProcessRecognitionConfig.Normalize(_config);
			_saveConfig?.Invoke(_config);
			if (_config.SyncWatermark) _onPropertiesWritten?.Invoke();
			_pendingAdditionalProperties.Clear();
			_edited = false;
			UpdatePreview();
			CaptureContext(document);

			string extraProperties = string.Join("、", values.Where(value => !value.Key.Equals(propertyName, StringComparison.CurrentCultureIgnoreCase)).Select(value => "“" + value.Key + "”"));
			string extraText = extraProperties.Length > 0 ? "，并写入" + extraProperties : "";
			_statusLabel.Text = summary.Length > 0 ? "已写入“" + propertyName + "”：" + summary + extraText : "已写入已确认属性" + extraText;
			_statusLabel.Text += "；实际更新 " + changed + " 项，请保存模型以保留。";
		}
		catch (Exception ex)
		{
			Logger.Error("Write recognized process property failed", ex);
			ToolNotice.Show(this, "写入失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	private List<RecognizedProcess> ReadRows()
	{
		return _grid.Rows.Cast<DataGridViewRow>()
			.Select(row =>
			{
				string confidenceText = row.Cells["Confidence"].Value?.ToString() ?? "";
				int.TryParse(confidenceText.TrimEnd('%'), out int confidence);
				return new RecognizedProcess
				{
					Name = row.Cells["Process"].Value?.ToString() ?? "",
					Confidence = confidence,
					Evidence = row.Cells["Evidence"].Value?.ToString() ?? ""
				};
			})
			.Where(row => !string.IsNullOrWhiteSpace(row.Name) && row.Name != "未知" && row.Name != "未识别")
			.ToList();
	}
}
