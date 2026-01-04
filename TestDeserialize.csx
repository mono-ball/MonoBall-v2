#!/usr/bin/env dotnet fsi

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization

type TileAnimationFrame = {
    [<JsonPropertyName("tileId")>]
    TileId: int
    [<JsonPropertyName("durationMs")>]
    DurationMs: int
}

type TilesetTile = {
    [<JsonPropertyName("localTileId")>]
    LocalTileId: int
    [<JsonPropertyName("animation")>]
    Animation: TileAnimationFrame[] option
}

type TilesetDefinition = {
    [<JsonPropertyName("id")>]
    Id: string
    [<JsonPropertyName("tiles")>]
    Tiles: TilesetTile[]
}

let json = File.ReadAllText("Mods/pokemon-emerald/Definitions/Assets/Maps/Tilesets/primary/general.json")
let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
let def = JsonSerializer.Deserialize<TilesetDefinition>(json, options)

printfn "Loaded tileset: %s" def.Id
printfn "Total tiles: %d" def.Tiles.Length

let animatedTiles = def.Tiles |> Array.filter (fun t -> t.Animation.IsSome && t.Animation.Value.Length > 0)
printfn "Animated tiles: %d" animatedTiles.Length

let tile79 = def.Tiles |> Array.tryFind (fun t -> t.LocalTileId = 79)
match tile79 with
| Some t ->
    printfn "Tile 79 found! Animation frames: %d" (t.Animation |> Option.map (fun a -> a.Length) |> Option.defaultValue 0)
| None ->
    printfn "Tile 79 NOT FOUND!"
