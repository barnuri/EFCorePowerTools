﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErikEJ.EFCorePowerTools.Services;
using RevEng.Common;
using RevEng.Common.Cli;
using RevEng.Core;
using Spectre.Console;

namespace ErikEJ.EFCorePowerTools.HostedServices;

#pragma warning disable CA1812 // Avoid uninstantiated internal classes
internal sealed class ScaffoldHostedService : HostedService
{
    private readonly IFileSystem fileSystem;
    private readonly ReverseEngineerCommandOptions reverseEngineerCommandOptions;
    private readonly ScaffoldOptions scaffoldOptions;
    private readonly TableListBuilder tableListBuilder;

    public ScaffoldHostedService(
        TableListBuilder tableListBuilder,
        IFileSystem fileSystem,
        ScaffoldOptions scaffoldOptions,
        ReverseEngineerCommandOptions reverseEngineerCommandOptions)
    {
        this.tableListBuilder = tableListBuilder;
        this.fileSystem = fileSystem;
        this.scaffoldOptions = scaffoldOptions;
        this.reverseEngineerCommandOptions = reverseEngineerCommandOptions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sw = Stopwatch.StartNew();
        var tableModels = GetTablesAndViews();
        GetProcedures(tableModels);
        GetFunctions(tableModels);
        sw.Stop();

        DisplayService.MarkupLine();
        DisplayService.MarkupLine(
            $"{tableModels.Count} database objects discovered in {sw.Elapsed.TotalSeconds:0.0} seconds",
            Color.Default);

        if (!CliConfigMapper.TryGetCliConfig(
                scaffoldOptions.ConfigFile.FullName,
                scaffoldOptions.ConnectionString,
                reverseEngineerCommandOptions.DatabaseType,
                tableModels,
                out var config))
        {
            Environment.ExitCode = 1;
            return;
        }

        var commandOptions = config.ToOptions(
            scaffoldOptions.ConnectionString,
            reverseEngineerCommandOptions.DatabaseType,
            Directory.GetCurrentDirectory(),
            scaffoldOptions.IsDacpac,
            scaffoldOptions.ConfigFile.FullName);
        DisplayService.MarkupLine();

        if (commandOptions.UseT4 && Constants.Version > 6)
        {
            var t4Result = T4Helper.DropT4Templates(commandOptions.ProjectPath);
            if (!string.IsNullOrEmpty(t4Result))
            {
                DisplayService.MarkupLine(t4Result, Color.Default);
            }
        }

        sw = Stopwatch.StartNew();
        var result = DisplayService.Wait(
            "Generating EF Core DbContext and entity classes...",
            () => ReverseEngineerRunner.GenerateFiles(commandOptions)) ?? new ReverseEngineerResult();
        sw.Stop();
        DisplayService.MarkupLine(
            $"{result.EntityTypeFilePaths.Count + result.ContextConfigurationFilePaths.Count + 1} files generated in {sw.Elapsed.TotalSeconds:0.0} seconds",
            Color.Default);
        DisplayService.MarkupLine();

        var paths = GetPaths(result);
        ShowPaths(paths);
        DisplayService.MarkupLine();

        ShowErrors(result);
        ShowWarnings(result);

        var redactedConnectionString = new ConnectionStringResolver(commandOptions.ConnectionString).Redact();

        var readmePath = Providers.CreateReadme(commandOptions, Constants.CodeGeneration, redactedConnectionString);
        var fileUri = new Uri(new Uri("file://"), readmePath);

        DisplayService.MarkupLine(
            "Thank you for using EF Core Power Tools, please open the readme file for next steps:", Color.Cyan1);
        DisplayService.MarkupLine($"{fileUri}", Color.Blue, DisplayService.Link);
        DisplayService.MarkupLine();

        Environment.ExitCode = 0;
    }

    private static void ShowPaths(List<string> paths)
    {
        foreach (var path in paths.Distinct())
        {
            DisplayService.MarkupLine(
                () => DisplayService.Markup("output folder:", Color.Green),
                () => DisplayService.Markup(path, Decoration.Bold));
        }
    }

    private static void ShowWarnings(ReverseEngineerResult result)
    {
        foreach (var warning in result.EntityWarnings)
        {
            DisplayService.MarkupLine(
                () => DisplayService.Markup("warning:", Color.Yellow),
                () => DisplayService.Markup(warning, Decoration.None));
        }
    }

    private static void ShowErrors(ReverseEngineerResult result)
    {
        foreach (var error in result.EntityErrors)
        {
            DisplayService.Error(error);
        }
    }

    private List<string> GetPaths(ReverseEngineerResult result)
    {
        var paths = new List<string> { Path.GetDirectoryName(result.ContextFilePath) };
        paths = paths.Concat(result.ContextConfigurationFilePaths.Select(p => fileSystem.Path.GetDirectoryName(p))
            .Distinct()).ToList();
        paths = paths.Concat(result.EntityTypeFilePaths.Select(p => fileSystem.Path.GetDirectoryName(p)).Distinct())
            .ToList();
        return paths;
    }

    private List<TableModel> GetTablesAndViews()
    {
        var tableModels = DisplayService.Wait("Getting database objects...", tableListBuilder.GetTableModels) ??
                          new List<TableModel>();

        var tableCount = tableModels.Count(t => t.ObjectType == ObjectType.Table);
        if (tableCount > 0)
        {
            DisplayService.MarkupLine($"{tableCount} tables found", Color.Default);
        }

        var viewCount = tableModels.Count(t => t.ObjectType == ObjectType.View);
        if (viewCount > 0)
        {
            DisplayService.MarkupLine($"{viewCount} views found", Color.Default);
        }

        return tableModels;
    }

    private void GetFunctions(List<TableModel> tableModels)
    {
        var functions = tableListBuilder.GetFunctions();
        tableModels.AddRange(functions);
        if (functions.Count > 0)
        {
            DisplayService.MarkupLine($"{functions.Count} functions found", Color.Default);
        }
    }

    private void GetProcedures(List<TableModel> tableModels)
    {
        var procedures = tableListBuilder.GetProcedures();
        tableModels.AddRange(procedures);
        if (procedures.Count > 0)
        {
            DisplayService.MarkupLine($"{procedures.Count} stored procedures found", Color.Default);
        }
    }
}
#pragma warning restore CA1812 // Avoid uninstantiated internal classes
