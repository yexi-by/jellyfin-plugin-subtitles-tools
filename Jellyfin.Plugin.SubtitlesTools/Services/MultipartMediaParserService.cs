using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SubtitlesTools.Models;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 根据当前媒体文件和同目录兄弟文件识别多分段电影结构。
/// </summary>
public sealed class MultipartMediaParserService
{
    private static readonly Regex PartPattern = new(
        @"^(?<base>.*?)(?:[\s._-]+)?(?<token>cd|part|pt|disc|disk)(?<number>\d{1,2})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mkv",
        ".avi",
        ".mov",
        ".wmv",
        ".m2ts",
        ".ts",
        ".mpg",
        ".mpeg",
        ".iso"
    };

    /// <summary>
    /// 解析当前媒体文件所在的分段组；若未识别到分段模式，则返回仅含当前文件的单分段结果。
    /// </summary>
    /// <param name="mediaPath">当前媒体文件完整路径。</param>
    /// <returns>分段组信息。</returns>
    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "这里的路径来自 Jellyfin 已入库媒体项，调用前已经校验为本地可读文件；本方法只在同目录内做兄弟文件识别。")]
    public MultipartMediaGroup Parse(string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            throw new ArgumentException("媒体路径不能为空。", nameof(mediaPath));
        }

        var currentFile = new FileInfo(mediaPath);
        if (!currentFile.Exists)
        {
            throw new FileNotFoundException("媒体文件不存在。", mediaPath);
        }

        if (!SupportedVideoExtensions.Contains(currentFile.Extension))
        {
            return CreateSinglePartGroup(currentFile);
        }

        var currentParse = ParseFileName(currentFile);
        if (currentParse is null || currentFile.Directory is null)
        {
            return CreateSinglePartGroup(currentFile);
        }

        var matchedParts = new List<(FileInfo File, ParsedPartInfo Info)>();
        foreach (var siblingFile in currentFile.Directory.EnumerateFiles())
        {
            if (!SupportedVideoExtensions.Contains(siblingFile.Extension)
                || !string.Equals(siblingFile.Extension, currentFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var siblingParse = ParseFileName(siblingFile);
            if (siblingParse is null)
            {
                continue;
            }

            if (!string.Equals(
                    siblingParse.CanonicalBaseName,
                    currentParse.CanonicalBaseName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matchedParts.Add((siblingFile, siblingParse));
        }

        if (matchedParts.Count <= 1)
        {
            return CreateSinglePartGroup(currentFile);
        }

        var orderedParts = matchedParts
            .OrderBy(item => item.Info.PartNumber)
            .ThenBy(item => item.File.Name, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => new MultipartMediaPart
            {
                Id = BuildStablePartId(item.File.FullName),
                Index = index,
                Label = BuildPartLabel(item.Info.PartKind, item.Info.PartNumber),
                PartKind = item.Info.PartKind,
                PartNumber = item.Info.PartNumber,
                MediaFile = item.File
            })
            .ToList();

        return new MultipartMediaGroup
        {
            CanonicalBaseName = currentParse.CanonicalBaseName,
            CurrentPartId = BuildStablePartId(currentFile.FullName),
            Parts = orderedParts
        };
    }

    private static MultipartMediaGroup CreateSinglePartGroup(FileInfo currentFile)
    {
        var currentPart = new MultipartMediaPart
        {
            Id = BuildStablePartId(currentFile.FullName),
            Index = 0,
            Label = "单文件",
            PartKind = "single",
            PartNumber = null,
            MediaFile = currentFile
        };

        return new MultipartMediaGroup
        {
            CanonicalBaseName = Path.GetFileNameWithoutExtension(currentFile.Name),
            CurrentPartId = currentPart.Id,
            Parts = [currentPart]
        };
    }

    private static ParsedPartInfo? ParseFileName(FileInfo file)
    {
        var fileBaseName = Path.GetFileNameWithoutExtension(file.Name);
        var match = PartPattern.Match(fileBaseName);
        if (!match.Success)
        {
            return null;
        }

        var canonicalBaseName = match.Groups["base"].Value.TrimEnd(' ', '.', '_', '-');
        if (string.IsNullOrWhiteSpace(canonicalBaseName))
        {
            return null;
        }

        return new ParsedPartInfo
        {
            CanonicalBaseName = canonicalBaseName,
            PartKind = NormalizePartKind(match.Groups["token"].Value),
            PartNumber = int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static string NormalizePartKind(string rawPartKind)
    {
        if (string.Equals(rawPartKind, "disk", StringComparison.OrdinalIgnoreCase))
        {
            return "disc";
        }

        return rawPartKind.Trim().ToLowerInvariant();
    }

    private static string BuildPartLabel(string partKind, int partNumber)
    {
        return partKind.ToUpperInvariant() switch
        {
            "PT" => $"PT{partNumber}",
            _ => $"{partKind.ToUpperInvariant()}{partNumber}"
        };
    }

    private static string BuildStablePartId(string fullPath)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
        return Convert.ToHexString(hashBytes[..8]).ToLowerInvariant();
    }

    /// <summary>
    /// 表示从文件名中解析出的分段模式。
    /// </summary>
    private sealed class ParsedPartInfo
    {
        /// <summary>
        /// 获取或设置规范化基名。
        /// </summary>
        public string CanonicalBaseName { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置分段类型。
        /// </summary>
        public string PartKind { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置分段编号。
        /// </summary>
        public int PartNumber { get; set; }
    }
}
