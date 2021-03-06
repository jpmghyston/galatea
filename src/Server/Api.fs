module Api

open System
open System.Collections.Generic
open MongoDB.Driver
open Shared

let getTechName (tech: string) =
    match tech with
    | "propulsion" -> "Hyperspace Range"
    | "scanning" -> "Scanning"
    | "terraforming" -> "Terraforming"
    | "research" -> "Experimentation"
    | "weapons" -> "Weapons"
    | "banking" -> "Banking"
    | "manufacturing" -> "Manufacturing"
    | _ -> invalidArg "tech" ("Invalid technology name: " + tech)

let getResearchEvents (tech: Dictionary<string, Snapshot.Tech>) (prevTech: Dictionary<string, Snapshot.Tech>) =
    tech
    |> Seq.map (fun pair -> (pair.Key, pair.Value))
    |> Seq.map (fun (name, tech) -> (name, tech.level, prevTech.Item(name).level))
    |> Seq.filter (fun (_, level, prevLevel) -> not (level = prevLevel))
    |> Seq.map (fun (name, level, prevLevel) -> Research { Tech = getTechName name; Level = level; })

let getCounterEvent (getValue: Snapshot.Player -> int) counter player prevPlayer  =
    match getValue player = getValue prevPlayer with
    | true -> None
    | false -> Some (Counter { Counter = counter; OldValue = getValue prevPlayer; NewValue = getValue player })

let getCounterEvents player prevPlayer: NewsfeedEvent seq =
    [
        getCounterEvent (fun player -> player.TotalEconomy) Economy;
        getCounterEvent (fun player -> player.TotalIndustry) Industry;
        getCounterEvent (fun player -> player.TotalScience) Science;
        getCounterEvent (fun player -> player.TotalStars) Stars;
        getCounterEvent (fun player -> player.TotalStrength) Ships;
    ] |> Seq.map (fun getCounter -> getCounter player prevPlayer) |> Seq.choose id

let getEvents (player: Snapshot.Player) (prevPlayer: Snapshot.Player): NewsfeedEvent list =
    [
        yield! getCounterEvents player prevPlayer;
        yield! getResearchEvents player.Tech prevPlayer.Tech
    ]

let getNewsfeedTick (prevSnapshot: Snapshot.Snapshot, snapshot: Snapshot.Snapshot): NewsfeedTick =
    let players = snapshot.Players.Values
                  |> Seq.zip prevSnapshot.Players.Values
                  |> Seq.map (fun (prevPlayer, player) -> { PlayerId = PlayerId player.Uid; Events = getEvents player prevPlayer })
                  |> Seq.filter (fun (player) -> not (Seq.isEmpty player.Events))
                  |> Seq.toList
    { Tick = snapshot.Tick; Players = players; Time = DateTimeOffset.FromUnixTimeMilliseconds(snapshot.Now) }

let getNewsfeed (): Newsfeed =
    let snapshots = Snapshot.collection.Find(fun _ -> true).ToList()
    let players = snapshots
                  |> Seq.sortByDescending (fun snapshot -> snapshot.Tick)
                  |> Seq.head
                  |> (fun snapshot -> snapshot.Players.Values)
                  |> Seq.map (fun player ->
                      { Id = PlayerId player.Uid
                        Name = player.Alias
                        Stars = player.TotalStars
                        Ships = player.TotalStrength
                        Economy = player.TotalEconomy
                        Industry = player.TotalIndustry
                        Science = player.TotalScience
                        Scanning = player.Tech.["propulsion"].level
                        HyperspaceRange = player.Tech.["scanning"].level
                        Terraforming = player.Tech.["terraforming"].level
                        Experimentation = player.Tech.["research"].level
                        Weapons = player.Tech.["weapons"].level
                        Banking = player.Tech.["banking"].level
                        Manufacturing = player.Tech.["manufacturing"].level })
                  |> Seq.map (fun player -> (player.Id, player))
                  |> Map.ofSeq

    let ticks = snapshots
                |> Seq.distinctBy (fun snapshot -> snapshot.Tick)
                |> Seq.sortBy (fun snapshot -> snapshot.Tick)
                |> Seq.pairwise
                |> (Seq.map getNewsfeedTick)
                |> Seq.toList

    { Players = players; Ticks = ticks }

let galateaApi: IGalateaApi = {
    newsfeed = fun () -> async { return getNewsfeed () }
}
