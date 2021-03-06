﻿module Logary.Tests.CoreTargets

open System
open System.Text.RegularExpressions
open System.Globalization
open Expecto
open Logary
open Logary.Targets
open Logary.Tests.Targets
open Hopac
open NodaTime
open TestDSL
open Fac

let textWriterConf () =
  let stdout, stderr = Fac.textWriter (), Fac.textWriter ()
  TextWriter.TextWriterConf.create(stdout, stderr)

module LiterateTesting =
  open System
  open LiterateConsole
  module Theme =
    let textColours = { foreground=ConsoleColor.White; background=None }
    let subtextColours = { foreground=ConsoleColor.Gray; background=None }
    let punctuationColours = { foreground=ConsoleColor.DarkGray; background=None }
    let levelVerboseColours = { foreground=ConsoleColor.Gray; background=None }
    let levelDebugColours = { foreground=ConsoleColor.Gray; background=None }
    let levelInfoColours = { foreground=ConsoleColor.White; background=None }
    let levelWarningColours = { foreground=ConsoleColor.Yellow; background=None }
    let levelErrorColours = { foreground=ConsoleColor.White; background=Some ConsoleColor.Red }
    let levelFatalColours = { foreground=ConsoleColor.White; background=Some ConsoleColor.Red }
    let keywordSymbolColours = { foreground=ConsoleColor.Blue; background=None }
    let numericSymbolColours = { foreground=ConsoleColor.Magenta; background=None }
    let stringSymbolColours = { foreground=ConsoleColor.Cyan; background=None }
    let otherSymbolColours = { foreground=ConsoleColor.Green; background=None }
    let nameSymbolColours = { foreground=ConsoleColor.Gray; background=None }
    let missingTemplateFieldColours = { foreground=ConsoleColor.Red; background=None }
    let theme = function
      | Tokens.Text -> textColours | Tokens.Subtext -> subtextColours | Tokens.Punctuation -> punctuationColours
      | Tokens.LevelVerbose -> levelVerboseColours | Tokens.LevelDebug -> levelDebugColours
      | Tokens.LevelInfo -> levelInfoColours | Tokens.LevelWarning -> levelWarningColours
      | Tokens.LevelError -> levelErrorColours | Tokens.LevelFatal -> levelFatalColours
      | Tokens.KeywordSymbol -> keywordSymbolColours | Tokens.NumericSymbol -> numericSymbolColours
      | Tokens.StringSymbol -> stringSymbolColours | Tokens.OtherSymbol -> otherSymbolColours
      | Tokens.NameSymbol -> nameSymbolColours | Tokens.MissingTemplateField -> missingTemplateFieldColours

  let levelD, levelE, levelF, levelI, levelV, levelW = "D", "E", "F", "I", "V", "W"
  let singleLetterLogLevelText = function
    | Debug -> levelD | Error -> levelE | Fatal -> levelF | Info -> levelI | Verbose -> levelV | Warn -> levelW

  let frenchFormatProvider = System.Globalization.CultureInfo("fr-FR") :> IFormatProvider

  let formatLocalTime provider (ens : EpochNanoSeconds) =
    let dto = DateTimeOffset.ofEpoch ens
    dto.LocalDateTime.ToString("HH:mm:ss", provider),
    Tokens.Subtext

  /// creates a LiterateConsoleConf for testing purposes, and also returns a function that,
  /// when invoked, returns the coloured text parts written to the target.
  let createInspectableWrittenPartsConf () =
    let writtenParts = ResizeArray<LiterateConsole.ColouredText>()
    // replace everything except for tokenize
    { LiterateConsole.empty with
        formatProvider  = frenchFormatProvider
        getLogLevelText = singleLetterLogLevelText
        formatLocalTime = formatLocalTime
        theme           = Theme.theme
        colourWriter    = fun sem parts -> writtenParts.AddRange(parts) },
    fun () -> writtenParts |> List.ofSeq

let timeMessage (nanos : int64) level =
  let value, units = Int64 nanos, Scaled (Seconds, float Constants.NanosPerSecond)
  snd (Message.time (PointName [| "A"; "B"; "C"; "Check" |]) (fun () -> 32) ())
  |> Message.setGauge (value, units)
  |> Message.setLevel level

let nanos xs =
  Duration.FromTicks (xs / Constants.NanosPerTick)

let helloWorldMsg =
  Message.eventX "Hello World!"
  >> Message.setTicksEpoch (0L : EpochNanoSeconds)

let levels expectedTimeText : LiterateConsole.ColouredText list =
  [ { text = "[";                    colours = LiterateTesting.Theme.punctuationColours }
    { text = expectedTimeText;       colours = LiterateTesting.Theme.subtextColours }
    { text = " ";                    colours = LiterateTesting.Theme.subtextColours }
    { text = LiterateTesting.levelI; colours = LiterateTesting.Theme.levelInfoColours }
    { text = "] ";                   colours = LiterateTesting.Theme.punctuationColours } ]

[<Tests>]
let tests =
  testList "CoreTargets" [
    Targets.basicTests "text writer" (fun name -> TextWriter.create (textWriterConf ()) name)
    Targets.integrationTests "text writer" (fun name -> TextWriter.create (textWriterConf ()) name)

    // TODO: don't want to actually print these
    //Targets.basicTests "literate console" (LiterateConsole.create LiterateConsole.empty)
    //Targets.integrationTests "literate console" (LiterateConsole.create LiterateConsole.empty)

    testCase "convert DateTimeOffset with timespan delta" <| fun _ ->
      let a = DateTimeOffset(2016, 07, 02, 12, 33, 56, TimeSpan.FromHours(2.0))
      let ts = a.timestamp
      let b = ts |> DateTimeOffset.ofEpoch
      Expect.equal a.Ticks b.Ticks "Should be convertible to timestamp and back, including accounting for TimeSpans"

    testCase "formatLocalTime" <| fun _ ->
      let actual, _ =
        let fp : IFormatProvider = upcast CultureInfo "sv-SE"
        LiterateConsole.empty.formatLocalTime
          fp (DateTimeOffset(2016, 10, 25, 11, 17, 41, TimeSpan(2,0,0)).timestamp)
      let expected = "13:17:41"
      Expect.isTrue (Regex.IsMatch(actual, @"\d{2}:\d{2}:\d{2}"))
        (sprintf "Should print the time without time zone, was: %s" actual)

    testList "literate console" [
      let testLiterateCase testMsg messageFactory cb =
        let conf, getWrittenParts = LiterateTesting.createInspectableWrittenPartsConf ()
        let message = messageFactory Info

        testCase testMsg <| fun () ->
          // in context
          let instance =
            LiterateConsole.create conf "testLC"
            |> Target.init emptyRuntime
            |> run

          start (instance.server (fun _ -> Job.result ()) None)

          // because
          message |> Target.logAndWait instance

          let written, expectedTimeText =
            getWrittenParts (),
            fst (LiterateTesting.formatLocalTime conf.formatProvider message.timestamp)

          // then
          try cb expectedTimeText written
          finally Target.finalise instance

      yield testLiterateCase "printing 'Hello World!'" helloWorldMsg <| fun expectedTimeText parts ->
        Expect.equal parts
                     [ yield! levels expectedTimeText
                       yield {text = "Hello World!"; colours = LiterateTesting.Theme.textColours } ]
                     "logging with info level and then finalising the target"

      yield testLiterateCase "Time in ms" (timeMessage 60029379L) <| fun expectedTimeText parts ->
        Expect.sequenceEqual
          parts
          [ yield! levels expectedTimeText
            yield { text = "A.B.C.Check"; colours = LiterateTesting.Theme.nameSymbolColours }
            yield { text = " took "; colours = LiterateTesting.Theme.subtextColours }
            yield { text = "60,03"; colours = LiterateTesting.Theme.numericSymbolColours }
            yield { text = " "; colours = LiterateTesting.Theme.subtextColours }
            yield { text = "ms"; colours = LiterateTesting.Theme.textColours }
            yield { text = " to execute."; colours = LiterateTesting.Theme.subtextColours } ]
          "Should print [06:15:02 DBG] A.B.C.Check took 60,02 ms"

      yield testLiterateCase "Time in μs" (timeMessage 133379L) <| fun expectedTimeText parts ->
        Expect.sequenceEqual
          parts
          [ yield! levels expectedTimeText
            yield { text = "A.B.C.Check"; colours = LiterateTesting.Theme.nameSymbolColours }
            yield { text = " took "; colours = LiterateTesting.Theme.subtextColours }
            yield { text = "133,38"; colours = LiterateTesting.Theme.numericSymbolColours }
            yield { text = " "; colours = LiterateTesting.Theme.subtextColours }
            yield { text = "µs"; colours = LiterateTesting.Theme.textColours }
            yield { text = " to execute."; colours = LiterateTesting.Theme.subtextColours } ]
          "Should print [06:15:02 DBG] A.B.C.Perform took 133,38 μs"

      yield testLiterateCase "Time in ns" (timeMessage 139L) <| fun expectedTimeText parts ->
        Expect.sequenceEqual
          parts
          [ yield! levels expectedTimeText
            yield { text = "A.B.C.Check"; colours = LiterateTesting.Theme.nameSymbolColours }
            yield { text = " took "; colours = LiterateTesting.Theme.subtextColours }
            yield { text = "139"; colours = LiterateTesting.Theme.numericSymbolColours }
            yield { text = " "; colours = LiterateTesting.Theme.subtextColours }
            yield { text = "ns"; colours = LiterateTesting.Theme.textColours }
            yield { text = " to execute."; colours = LiterateTesting.Theme.subtextColours } ]
          "Should print [06:15:02 DBG] A.B.C.Perform took 139 μs, because nanoseconds is as accurate as it gets"
    ]

    testList "text writer prints" [
      testCase "message" <| fun _ ->
        let stdout = Fac.textWriter ()
        let target = TextWriter.create (TextWriter.TextWriterConf.create(stdout, stdout)) "writing console target"
        let instance = target |> Target.init emptyRuntime |> run
        instance.server (fun _ -> Job.result ()) None
        |> start

        (because "logging with info level and then finalising the target" <| fun () ->
          Message.eventInfo "Hello World!" |> Target.logAndWait instance
          Target.finalise instance
          stdout.ToString())
        |> should contain "Hello World!"
        |> thatsIt

      testCase "fields" <| fun _ ->
        let stdout = Fac.textWriter ()
        let target = TextWriter.create (TextWriter.TextWriterConf.create(stdout, stdout)) "writing console target"
        let instance = target |> Target.init emptyRuntime |> run
        instance.server (fun _ -> Job.result ()) None
        |> start

        let x = dict ["foo", "bar"]
        (because "logging with fields then finalising the target" <| fun () ->
          Message.event Info "textwriter-test-init" |> Message.setFieldFromObject "the Name" x |> Target.logAndWait instance
          Target.finalise instance
          stdout.ToString())
        |> should contain "textwriter-test-init"
        |> should contain "foo"
        |> should contain "bar"
        |> should contain "the Name"
        |> thatsIt

      testCase "to correct stream" <| fun _ ->
        let out, err = Fac.textWriter (), Fac.textWriter ()
        let target = TextWriter.create (TextWriter.TextWriterConf.create(out, err)) "error writing"
        let subject = target |> Target.init emptyRuntime |> run
        subject.server (fun _ -> Job.result ()) None
        |> start

        (because "logging 'Error line' and 'Fatal line' to the target" <| fun () ->
          Message.eventError "Error line" |> Target.logAndWait subject
          Message.eventFatal "Fatal line" |> Target.logAndWait subject
          Target.finalise subject
          err.ToString())
        |> should contain "Error line"
        |> should contain "Fatal line"
        |> thatsIt
      ]
    ]
