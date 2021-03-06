﻿// Learn more about F# at http://fsharp.org

open System
open Microsoft.ML
open Microsoft.ML.Core.Data
open Microsoft.ML.Data
open System.IO
open DataStructures

let modelsLocation = @"../../../../MLModels"

let datasetsLocation = @"../../../../Data"
let trainingDataLocation = sprintf @"%s/hour_train.csv" datasetsLocation
let testDataLocation = sprintf @"%s/hour_test.csv" datasetsLocation



let read (dataPath : string) (dataLoader : TextLoader) =
    dataLoader.Read dataPath

/// Cast ML.NET pipeline object to IEstimator<ITransformer> interface
let downcastPipeline (pipeline : IEstimator<'a>) =
    match pipeline with
    | :? IEstimator<ITransformer> as p -> p
    | _ -> failwith "The pipeline has to be an instance of IEstimator<ITransformer>."


[<EntryPoint>]
let main argv =
    
    // Create MLContext to be shared across the model creation workflow objects 
    // Set a random seed for repeatable/deterministic results across multiple trainings.
    let mlContext = MLContext(seed = Nullable 0)

    // 1. Common data loading configuration
    let textLoader = 
        mlContext.Data.CreateTextReader(
            columns = 
                [|
                    TextLoader.Column("Season", Nullable DataKind.R4, 2)
                    TextLoader.Column("Year", Nullable DataKind.R4, 3)
                    TextLoader.Column("Month", Nullable DataKind.R4, 4)
                    TextLoader.Column("Hour", Nullable DataKind.R4, 5)
                    TextLoader.Column("Holiday", Nullable DataKind.R4, 6)
                    TextLoader.Column("Weekday", Nullable DataKind.R4, 7)
                    TextLoader.Column("WorkingDay", Nullable DataKind.R4, 8)
                    TextLoader.Column("Weather", Nullable DataKind.R4, 9)
                    TextLoader.Column("Temperature", Nullable DataKind.R4, 10)
                    TextLoader.Column("NormalizedTemperature", Nullable DataKind.R4, 11)
                    TextLoader.Column("Humidity", Nullable DataKind.R4, 12)
                    TextLoader.Column("Windspeed", Nullable DataKind.R4, 13)
                    TextLoader.Column("Count", Nullable DataKind.R4, 16)
                |],
            hasHeader = true,
            separatorChar = ','
        )
              
    let trainingDataView = textLoader.Read(trainingDataLocation)
    let testDataView = textLoader.Read(testDataLocation)


    // 2: Common data process configuration with pipeline data transformations
    let dataProcessPipeline =
        (mlContext.Transforms.CopyColumns("Count", "Label") |> downcastPipeline)
            .Append(mlContext.Transforms.Concatenate("Features", "Season", "Year", "Month",
                                                     "Hour", "Holiday", "Weekday",
                                                     "Weather", "Temperature", "NormalizedTemperature",
                                                     "Humidity", "Windspeed"))
            .AppendCacheCheckpoint(mlContext)
        |> downcastPipeline

    // (Optional) Peek data in training DataView after applying the ProcessPipeline's transformations  
    Common.ConsoleHelper.peekDataViewInConsole<DataStructures.DemandObservation> mlContext trainingDataView dataProcessPipeline 10 |> ignore
    Common.ConsoleHelper.peekVectorColumnDataInConsole mlContext "Features" trainingDataView dataProcessPipeline 10 |> ignore

    // Definition of regression trainers/algorithms to use
    let regressionLearners : (string * IEstimator<ITransformer>) array =
        [|
            "FastTree", mlContext.Regression.Trainers.FastTree() |> downcastPipeline
            "Poisson", mlContext.Regression.Trainers.PoissonRegression() |> downcastPipeline
            "SDCA", mlContext.Regression.Trainers.StochasticDualCoordinateAscent() |> downcastPipeline
            "FastTreeTweedie", mlContext.Regression.Trainers.FastTreeTweedie() |> downcastPipeline
            //Other possible learners that could be included
            //...FastForestRegressor...
            //...GeneralizedAdditiveModelRegressor...
            //...OnlineGradientDescent... (Might need to normalize the features first)
        |]

    // 3. Phase for Training, Evaluation and model file persistence
    // Per each regression trainer: Train, Evaluate, and Save a different model
    for (learnerName, trainer) in regressionLearners do
        printfn "================== Training the current model =================="
        let trainingPipeline = dataProcessPipeline.Append(trainer)
        let trainedModel = trainingPipeline.Fit(trainingDataView)
        
        printfn "===== Evaluating Model's accuracy with Test data ====="
        let predictions = trainedModel.Transform(testDataView)
        let metrics = mlContext.Regression.Evaluate(predictions, label = "Count", score = "Score")
        Common.ConsoleHelper.printRegressionMetrics learnerName metrics
        


        //Save the model file that can be used by any application
        let modelPath = sprintf "%s/%sModel.zip" modelsLocation learnerName
        use fs = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.Write)
        mlContext.Model.Save(trainedModel, fs);

        printfn "The model is saved to %s" modelPath
 
    // 4. Try/test Predictions with the created models
    // The following test predictions could be implemented/deployed in a different application (production apps)
    // that's why it is seggregated from the previous loop
    // For each trained model, test 10 predictions           
    for (learnerName, _) in regressionLearners do
        //Load current model from .ZIP file
        let modelPath = sprintf "%s/%sModel.zip" modelsLocation learnerName
        use stream = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read)
        let trainedModel = mlContext.Model.Load stream

        // Create prediction engine related to the loaded trained model
        let predEngine = trainedModel.CreatePredictionEngine<DemandObservation, DemandPrediction>(mlContext)
        printfn "================== Visualize/test 10 predictions for model %sModel.zip ==================" learnerName

        //Visualize 10 tests comparing prediction with actual/observed values from the test dataset
        ModelScoringTester.visualizeSomePredictions testDataLocation predEngine 10

    Common.ConsoleHelper.consolePressAnyKey ()

    0 // return an integer exit code
