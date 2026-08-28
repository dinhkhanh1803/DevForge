namespace TeamTool.Desktop;

public sealed class MainForm : Form
{
    public MainForm(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        Name = "MainForm";
        Text = "TeamTool";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 360);
        MinimumSize = new Size(480, 280);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 3,
            TabStop = false,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "TeamTool - native Windows tool",
            AutoSize = true,
            Dock = DockStyle.Fill,
            TabStop = false,
        };
        var status = new Label
        {
            Name = "StatusLabel",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            TabStop = false,
        };
        status.DataBindings.Add(nameof(Label.Text), viewModel, nameof(MainViewModel.Status),
            formattingEnabled: false, DataSourceUpdateMode.Never);
        var refresh = new Button
        {
            Name = "RefreshButton",
            Text = "&Refresh",
            AccessibleName = "Refresh status",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            TabIndex = 0,
        };
        refresh.Click += (_, _) => viewModel.RefreshCommand.Execute(null);
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(status, 0, 1);
        layout.Controls.Add(refresh, 0, 2);
        Controls.Add(layout);
        AcceptButton = refresh;
    }
}
