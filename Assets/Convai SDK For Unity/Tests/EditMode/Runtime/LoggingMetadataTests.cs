using System;
using System.Reflection;
using Convai.Domain.Logging;
using Convai.Runtime;
using Convai.Runtime.Logging;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Runtime
{
    [TestFixture]
    public class LoggingMetadataTests
    {
        [SetUp]
        public void SetUp()
        {
            _sink = new TestLogSink();
            _settings = ConvaiSettings.Instance;
            Assert.IsNotNull(_settings, "ConvaiSettings instance must exist for logging metadata tests.");

            _originalGlobalLevel = _settings.GlobalLogLevel;
            _originalCategoryOverrides = CloneOverrides(_settings.CategoryOverrides);
            _originalIncludeStackTraces = _settings.IncludeStackTraces;
            _originalColoredOutput = _settings.ColoredOutput;

            ConvaiLogger.ClearSinks();
            ConvaiLogger.Initialize();
            ConvaiLogger.RegisterSink(_sink);
            LoggingConfig.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null)
            {
                _settings.SetGlobalLogLevel(_originalGlobalLevel);
                _settings.SetCategoryOverrides(CloneOverrides(_originalCategoryOverrides));
                SetPrivateField(_settings, "_includeStackTraces", _originalIncludeStackTraces);
                SetPrivateField(_settings, "_coloredOutput", _originalColoredOutput);
                LoggingConfig.InvalidateCache();
            }

            ConvaiLogger.ClearSinks();
            _sink?.Dispose();
        }

        private TestLogSink _sink;
        private ConvaiSettings _settings;
        private LogLevel _originalGlobalLevel;
        private LogLevelOverride[] _originalCategoryOverrides;
        private bool _originalIncludeStackTraces;
        private bool _originalColoredOutput;

        [Test]
        public void ConvaiLogger_InfoWithLipSyncCategory_FormatsLipSyncCategoryName()
        {
            const string message = "Lip sync metadata test";

            ConvaiLogger.Info(message, LogCategory.LipSync);

            Assert.That(_sink.Entries.Count, Is.GreaterThanOrEqualTo(1));

            LogEntry entry = _sink.Entries.Find(candidate => candidate.Message == message);
            Assert.That(entry.Category, Is.EqualTo(LogCategory.LipSync));

            var consoleSink = new UnityConsoleSink();
            MethodInfo formatMethod = typeof(UnityConsoleSink).GetMethod(
                "FormatLogEntry",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(formatMethod, "Expected UnityConsoleSink.FormatLogEntry to exist.");

            string formatted = (string)formatMethod.Invoke(consoleSink, new object[] { entry });
            Assert.That(formatted, Does.Contain("[LipSync]"));
        }

        [Test]
        public void LoggingConfig_IsEnabled_RespectsLipSyncOverride()
        {
            _settings.SetGlobalLogLevel(LogLevel.Info);
            _settings.SetCategoryOverrides(new[] { new LogLevelOverride(LogCategory.LipSync, LogLevel.Error) });
            LoggingConfig.InvalidateCache();

            Assert.That(LoggingConfig.IsEnabled(LogLevel.Error, LogCategory.LipSync), Is.True);
            Assert.That(LoggingConfig.IsEnabled(LogLevel.Info, LogCategory.LipSync), Is.False);
            Assert.That(LoggingConfig.IsEnabled(LogLevel.Debug, LogCategory.LipSync), Is.False);
            Assert.That(LoggingConfig.IsEnabled(LogLevel.Info, LogCategory.SDK), Is.True);
        }

        [Test]
        public void UnityConsoleSink_FormatLogEntry_RespectsColoredOutputSetting()
        {
            SetPrivateField(_settings, "_coloredOutput", false);
            LoggingConfig.InvalidateCache();

            var entry = LogEntry.Info(LogCategory.SDK, "Colored output test");
            var consoleSink = new UnityConsoleSink();
            MethodInfo formatMethod = typeof(UnityConsoleSink).GetMethod(
                "FormatLogEntry",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(formatMethod, "Expected UnityConsoleSink.FormatLogEntry to exist.");

            string formatted = (string)formatMethod.Invoke(consoleSink, new object[] { entry });

            Assert.That(formatted, Does.Not.Contain("<color="));
        }

        [Test]
        public void UnityConsoleSink_FormatLogEntry_RespectsIncludeStackTracesSetting()
        {
            Exception exception = CreateExceptionWithStackTrace();

            SetPrivateField(_settings, "_includeStackTraces", false);
            LoggingConfig.InvalidateCache();

            LogEntry entry = LogEntry.CreateWithException(LogLevel.Error, LogCategory.SDK, "Stack trace test",
                exception);
            var consoleSink = new UnityConsoleSink();
            MethodInfo formatMethod = typeof(UnityConsoleSink).GetMethod(
                "FormatLogEntry",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(formatMethod, "Expected UnityConsoleSink.FormatLogEntry to exist.");

            string formatted = (string)formatMethod.Invoke(consoleSink, new object[] { entry });

            Assert.That(formatted, Does.Contain(exception.GetType().Name));
            Assert.That(formatted, Does.Contain(exception.Message));
            Assert.That(formatted, Does.Not.Contain(exception.StackTrace));
        }

        private static LogLevelOverride[] CloneOverrides(LogLevelOverride[] source)
        {
            if (source == null || source.Length == 0) return Array.Empty<LogLevelOverride>();

            var copy = new LogLevelOverride[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static Exception CreateExceptionWithStackTrace()
        {
            try
            {
                ThrowForStackTrace();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static void ThrowForStackTrace() => throw new InvalidOperationException("Stack trace test");

        private static void SetPrivateField(ConvaiSettings settings, string fieldName, object value)
        {
            FieldInfo field =
                typeof(ConvaiSettings).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected ConvaiSettings.{fieldName} to exist.");
            field.SetValue(settings, value);
        }
    }
}
