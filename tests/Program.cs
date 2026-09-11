using System.Text.Json;
using Finestats.Config;
using Finestats.Events;
using Finestats.Services;
using Finestats.Storage;
using Microsoft.Data.Sqlite;

if (args.Length == 2 && args[0] == "--write-config")
{
    File.WriteAllText(args[1], "// finestats-lite: local SQLite, no backend. Reload plugin after edits.\n" + JsonSerializer.Serialize(new StatsConfig(),new JsonSerializerOptions {WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping})+"\n");
    return;
}
if (args.Length == 2 && args[0] == "--validate-config")
{
    var candidate = StatsConfig.Read(args[1]); candidate.Validate();
    Console.WriteLine($"Lite config valid: {candidate.Messages.Count} templates; bots excluded: {candidate.Ranking.ExcludeBots}; minimum kills: {candidate.Ranking.MinimumKillsForLeaderboard}.");
    return;
}
int checks=0;
void Check(bool value,string name) { if(!value) throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name);checks++; }
var directory=Path.Combine(Path.GetTempPath(),"finestats-lite-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
var config=new StatsConfig {BackupIntervalMinutes=0,Ranking=new RankingOptions {ExcludeBots=false,MinimumKillsForLeaderboard=3}};
config.Validate();
Check(PlaytimeFormatter.Format(0)=="0 minutes" && PlaytimeFormatter.Format(59)=="0 minutes","playtime below a minute is not rounded up");
Check(PlaytimeFormatter.Format(60)=="1 minute" && PlaytimeFormatter.Format(3600)=="1 hour","playtime uses singular units");
Check(PlaytimeFormatter.Format(90060)=="1 day 1 hour 1 minute" && PlaytimeFormatter.Format(180120)=="2 days 2 hours 2 minutes","playtime expands days hours and minutes");
Check(PlaytimeFormatter.Format(null)=="—" && PlaytimeFormatter.Format(double.NaN)=="—","unknown playtime remains unknown");
Check(StatsCommandClient.ChatLine("hello").StartsWith("[blue][fs][/] "),"fallback prefix uses blue fs");
var joinGate=new ConnectionGate();
Check(!joinGate.TryBegin(1,0,true),"authentication before joining does not announce");
joinGate.Ready(1,0);Check(!joinGate.TryBegin(1,0,false) && joinGate.TryBegin(1,0,true),"joining before authentication waits and supports native session zero");
Check(!joinGate.TryBegin(1,0,true),"duplicate native callbacks announce only once");
joinGate.Ready(1,1);Check(!joinGate.TryBegin(1,0,true) && joinGate.TryBegin(1,1,true),"slot reuse rejects previous native session");
joinGate.Remove(1);Check(!joinGate.TryBegin(1,1,true),"disconnect invalidates pending join state");
var welcome=config.ConnectionMessages.Format("[red]Alice[/]", "DE", 4, config);
Check(ChatColors.Plain(welcome)=="Player Alice connected from Germany (DE) - Rank #4","colored connect message shows English country and rank");
Check(config.ConnectionMessages.Format("Alice",null,null,config).Contains("Unknown country") && config.ConnectionMessages.Format("Alice",null,null,config).Contains("Unranked"),"connect message has honest unknown fallbacks");
Check(config.ConnectionMessages.Format("Alice","DE",1,config with {ConnectCountryNames=false}).Contains("[yellow]DE[/]"),"country code display configurable");
try {(config with {ConnectionMessages=new() {Message="{password}"}}).Validate();throw new Exception("Expected invalid template");}catch(InvalidDataException){Check(true,"unknown connect placeholder rejected");}
Check((new ConnectionMessages {Message=""}).Format("Alice","DE",1,config)=="","empty connect template suppresses output");
var collector=Guid.NewGuid();var round=Guid.NewGuid();long sequence=0;
var alice=new PlayerIdentity(1,"alice-session","76561198000000001","Alice",2,false,true);
var bob=new PlayerIdentity(2,"bob-session","76561198000000002","Bob",3,false,true);
var bot=new PlayerIdentity(3,"bot-session",null,"Bot",3,true,false);
StatsEvent Event(string type,EventData data,bool? warmup=false) => new(type,1,config.ServerId,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),"de_dust2",1,data,Guid.NewGuid(),collector,++sequence,Guid.NewGuid(),Guid.NewGuid(),round,10,warmup);
StatsEvent Kill(PlayerIdentity? a=null,PlayerIdentity? v=null,PlayerIdentity? assist=null,bool team=false,bool suicide=false) => Event("kill",new KillEvent(a??alice,v??bot,assist,"ak47",true,0,false,false,null,false,null,false,false,team,suicide,false,false));
async Task<JsonElement> Profile(LiteStore store,PlayerIdentity player) => (await store.ReadAsync("players/"+player.Steamid,default))!.Value;
async Task<long> Kills(LiteStore store,PlayerIdentity player) => (await Profile(store,player)).GetProperty("kills").GetInt64();
try
{
    var database=Path.Combine(directory,"stats.db");var store=new LiteStore(database,config);
    var start=Event("session_start",new PlayerEvent(alice,DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds(),"connect"));
    await store.AppendAsync([start],default);
    var first=Kill();var second=Kill();
    var notices=await store.AppendAsync([first,second],default);
    Check(notices.Length==2 && notices.All(n=>n.Delta>0 && n.Steamid==alice.Steamid),"included bots award human points only");
    Check(notices.Select(n=>n.KillsAfter).SequenceEqual(new long?[]{1,2}),"per-kill progress within one transaction");
    Check((await Profile(store,alice)).GetProperty("rank").ValueKind==JsonValueKind.Null,"below threshold remains unranked");
    Check(await Kills(store,alice)==2,"kills stored locally");
    Check((await store.AppendAsync([first,second],default)).SequenceEqual(notices) && await Kills(store,alice)==2,"duplicate events preserve receipts without recounting");
    var third=await store.AppendAsync([Kill()],default);
    Check(third.Single().KillsAfter==3 && (await Profile(store,alice)).GetProperty("rank").GetInt64()==1,"threshold kill qualifies player");
    var restarted=new LiteStore(database,config);
    Check(await Kills(restarted,alice)==3,"restart preserves SQLite data");
    Check((await restarted.AppendAsync([first],default)).Single()==notices[0],"deduplication survives restart");
    var oldKills=await Kills(store,alice);
    await new LiteStore(database,config with {Ranking=config.Ranking with {ExcludeBots=true}}).AppendAsync([Kill()],default);
    Check(await Kills(store,alice)==oldKills,"configured bot exclusion");
    await store.AppendAsync([Kill() with {Warmup=true},Kill() with {Warmup=null}],default);
    Check(await Kills(store,alice)==oldKills,"warmup and unknown warmup excluded");
    var beforePoints=(await Profile(store,alice)).GetProperty("points").GetDouble();
    await store.AppendAsync([Kill(v:bob with {Team=2},team:true),Kill(v:alice,suicide:true)],default);
    Check(await Kills(store,alice)==oldKills && (await Profile(store,alice)).GetProperty("points").GetDouble()==beforePoints-15,"teamkill and suicide penalties do not advance kills");
    var floor=new LiteStore(database,config with {Ranking=config.Ranking with {DeathBase=100000}});
    await floor.AppendAsync([Kill(a:bot with {Team=2},v:alice with {Team=3})],default);
    Check((await Profile(store,alice)).GetProperty("points").GetDouble()==0,"bot attacker uses baseline strength and respects points floor");
    var pending=alice with {Steamid=null,Authenticated=false,SessionId="pending"};
    await store.AppendAsync([Kill(a:pending)],default);
    await store.AppendAsync([Event("player_authenticated",new PlayerEvent(pending with {Steamid=alice.Steamid,Authenticated=true},1,"auth"))],default);
    Check(await Kills(store,alice)==oldKills+1,"late Steam authentication merges provisional counters");
    var hit=Event("hit",new HitEvent(alice,bot,"ak47",25,0,75,0,1,"head",false,false));
    await store.AppendAsync([hit,Event("shot",new ShotEvent(alice,"ak47",false)),Event("mvp",new ObjectiveEvent(alice))],default);
    using var commands=new StatsCommandClient(config,store);
    var who=new CommandPlayer(alice.Steamid!,alice.SessionId,collector);
    foreach(var command in StatsCommandClient.Names)
    {
        var lines=await commands.Execute(command,[],who,default);
        Check(lines.Length>0 && lines.All(x=>!x.Contains("{remaining}")),"local command "+command);
    }
    foreach (bool publicRank in new[] { true, false })
    {
        using var visibilityCommands = new StatsCommandClient(config with { PublicRankReplies = publicRank }, store);
        foreach (var command in StatsCommandClient.Names)
        {
            bool publicReply = false;
            await visibilityCommands.Execute(command, [], who, default, () => publicReply = true);
            Check(publicReply == (command == "statsme" || command == "rank" && publicRank),
                $"{command} visibility with PublicRankReplies={publicRank}");
        }

        bool invalidPublic = false;
        await visibilityCommands.Execute("statsme", ["unexpected"], who, default, () => invalidPublic = true);
        Check(!invalidPublic, "statsme argument errors stay private");
        bool missingPublic = false;
        await visibilityCommands.Execute("statsme", [], who with { Steamid = "76561198000009999" }, default, () => missingPublic = true);
        Check(!missingPublic, "statsme missing profile stays private");
    }
    var session=(await store.ReadAsync($"players/{alice.Steamid}/sessions?session={alice.SessionId}&collector={collector}",default))!.Value;
    Check(session.GetProperty("items")[0].GetProperty("counters").GetProperty("kills").GetInt64()==3,"exact session counters independent of merged historical sessions");
    var missing=(await store.ReadAsync($"players/{alice.Steamid}/sessions?session=other&collector={collector}",default))!.Value;
    Check(missing.GetProperty("items").GetArrayLength()==0,"session routing excludes stale sessions");
    await store.AppendAsync([Event("player_country",new PlayerEvent(alice with {Geoip=new(1,"DE")},1,"country"))],default);
    Check((await Profile(store,alice)).GetProperty("country_code").GetString()=="DE","local GeoIP stores country only");
    var rollbackKill=Kill();
    try { await store.AppendAsync([rollbackKill,Kill() with {ServerId="wrong"}],default); throw new Exception("Expected rejection"); } catch(InvalidDataException) { }
    var beforeRetry=await Kills(store,alice);await store.AppendAsync([rollbackKill],default);
    Check(await Kills(store,alice)==beforeRetry+1,"failed batch rolls back receipt and statistics atomically");
    var backup=await store.BackupAsync();
    var restored=new LiteStore(backup,config);
    Check(await Kills(restored,alice)==await Kills(store,alice),"online backup restores committed state");
    Check((await restored.AppendAsync([first],default)).Single()==notices[0],"backup retains deduplication");
    using(var c=new SqliteConnection("Data Source="+database))
    {
        c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA integrity_check";Check((string)cmd.ExecuteScalar()! == "ok","SQLite integrity check");
        cmd.CommandText="SELECT count(*) FROM players WHERE steam IS NULL AND points<>1000";Check((long)cmd.ExecuteScalar()! ==0,"bots and provisional identities never gain rating");
    }
    var invalid=config with {DatabaseFile="../outside.db"};try {invalid.Validate();throw new Exception("Expected invalid path");}catch(InvalidDataException){Check(true,"database filename confined to plugin data");}
    var queue=new EventQueue(100);var log=new Diagnostics(_=>{},null,30);
    var worker=new EventDispatcher(queue,new SqliteStatsClient(config,store),config with {FlushIntervalMs=100},log);
    worker.Start();queue.TryEnqueue(Kill());await worker.RequestStop();
    Check(worker.Sent==1 && worker.Failed==0,"background dispatcher drains SQLite on unload");
    var concurrentEvents=Enumerable.Range(0,12).Select(_=>Kill()).ToArray();
    var beforeConcurrent=await Kills(store,alice);
    await Task.WhenAll(concurrentEvents.Select(e=>Task.Run(()=>new LiteStore(database,config).AppendAsync([e],default))));
    Check(await Kills(store,alice)==beforeConcurrent+12,"overlapping writer instances serialize without lost counters");
    await Task.WhenAll(Enumerable.Range(0,6).Select(_=>Task.Run(async ()=>Check((await Profile(store,alice)).GetProperty("rank").GetInt64()>0,"concurrent local read"))));
    using(var cancelled=new CancellationTokenSource())
    {
        cancelled.Cancel();
        try {await store.AppendAsync([Kill()],cancelled.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){Check(true,"cancelled writes do not start a transaction");}
    }
    var kept=new LiteStore(database,config with {BackupKeepCount=2});
    await kept.BackupAsync();await kept.BackupAsync();await kept.BackupAsync();
    Check(Directory.GetFiles(Path.Combine(directory,"backups"),"stats.db.*.db").Length==2,"backup retention keeps configured snapshot count");
    var future=Path.Combine(directory,"future.db");
    using(var c=new SqliteConnection("Data Source="+future)){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA user_version=99";cmd.ExecuteNonQuery();}
    try {await new LiteStore(future,config).AppendAsync([Kill()],default);throw new Exception("Expected refusal");}catch(InvalidDataException){Check(true,"unsupported future database refused");}
    var foreign=Path.Combine(directory,"foreign.db");
    using(var c=new SqliteConnection("Data Source="+foreign)){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE important_data(value TEXT); INSERT INTO important_data VALUES('keep')";cmd.ExecuteNonQuery();}
    try {await new LiteStore(foreign,config).AppendAsync([Kill()],default);throw new Exception("Expected refusal");}catch(InvalidDataException){Check(true,"foreign SQLite database refused without overwriting tables");}
    try {await new LiteStore(database,config with {ServerId="other"}).AppendAsync([],default);throw new Exception("Expected refusal");}catch(InvalidDataException){Check(true,"database bound to one server");}
    var partialConfig=Path.Combine(directory,"config.jsonc");File.WriteAllText(partialConfig,"{\"Messages\":{\"RankReply\":\"{name}: {summary}\"}}");
    Check(StatsConfig.Read(partialConfig).Messages["NoSession"].Contains("locally"),"partial config retains Lite-specific message defaults");
    var noScoreConfig=config with {ScoreNotificationsEnabled=false,Ranking=config.Ranking with {KillBase=0,HeadshotBonus=0,MinimumKillsForLeaderboard=100}};
    ScoreReceipt[] delivered=[];
    using(var client=new SqliteStatsClient(noScoreConfig,new LiteStore(database,noScoreConfig),r=>delivered=r))
    {
        var zero=Kill();var outcome=await client.SendAsync([zero],default);
        Check(outcome.Accepted && delivered.Single().Delta==0 && ScoreReceiptFilter.ProgressMessage(delivered.Single(),noScoreConfig).Length>0,"zero-score local commit still delivers progress");
        delivered=[];await client.SendAsync([zero],default);Check(delivered.Length==0,"local notification retries are deduplicated");
    }
    var rollbackBefore=await Kills(store,alice);
    using(var client=new SqliteStatsClient(config,store,_=>throw new IOException("callback")))
        Check((await client.SendAsync([Kill()],default)).Accepted && await Kills(store,alice)==rollbackBefore+1,"failed chat callback cannot retry committed SQLite batch");
    var whole = new LiteStore(Path.Combine(directory,"whole.db"),config);
    var qualificationConfig = config with { Ranking = config.Ranking with { MinimumKillsForLeaderboard = 10 } };
    var qualificationStore = new LiteStore(Path.Combine(directory, "qualification.db"), qualificationConfig);
    for (int killNumber = 1; killNumber <= 11; killNumber++)
    {
        var killEvent = Kill();
        var receipt = (await qualificationStore.AppendAsync([killEvent], default)).Single();
        Check(receipt.Delta > 0 && receipt.KillsAfter == killNumber, $"kill {killNumber} still records points");
        Check((ScoreReceiptFilter.Message(receipt, qualificationConfig).Length > 0) == (killNumber >= 10),
            $"kill {killNumber} score notice respects qualification");
        Check((ScoreReceiptFilter.ProgressMessage(receipt, qualificationConfig).Length > 0) == (killNumber <= 10),
            $"kill {killNumber} progress notice remains available");
        var silentConfig = qualificationConfig with { RankingProgressNotificationsEnabled = false };
        var envelope = JsonSerializer.SerializeToElement(new { scoreNotices = new[] { receipt } });
        var filtered = new ScoreReceiptFilter(silentConfig).Read(envelope, [killEvent], killEvent.Timestamp);
        Check(filtered.Length == (killNumber >= 10 ? 1 : 0), $"kill {killNumber} delivery filter respects qualification");
        Check(ScoreReceiptFilter.Message(receipt, qualificationConfig with
        {
            Ranking = qualificationConfig.Ranking with { MinimumKillsForLeaderboard = 0 }
        }).Length > 0, "zero minimum permits immediate kill notices");
    }
    var normal = Kill(v:bot with {Name="[red]Opponent {points}[/]"});
    normal=normal with {Data=((KillEvent)normal.Data) with {Headshot=false}};
    var normalReceipt=(await whole.AppendAsync([normal],default)).Single();
    Check(normalReceipt.Delta==5 && normalReceipt.PointsAfter==1005 && normalReceipt.Headshot==false,"normal kill books five whole points at equal strength");
    Check(ChatColors.Plain(ScoreReceiptFilter.Message(normalReceipt with { KillsAfter = 3 },config))=="Received 5 Points for Killing Opponent points (1005)","normal kill message sanitizes opponent without expanding tokens");
    var headshotEvent=Kill();var headshotReceipt=(await whole.AppendAsync([headshotEvent],default)).Single();
    Check(headshotReceipt.Delta==7 && headshotReceipt.PointsAfter==1012 && headshotReceipt.Headshot==true,"headshot includes two-point bonus and books rounded total");
    Check(ChatColors.Plain(ScoreReceiptFilter.Message(headshotReceipt with { KillsAfter = 3 },config))=="Received 7 Points for Killing Bot with Headshot (1012)","headshot uses exactly one dedicated score message");
    Check((await new LiteStore(Path.Combine(directory,"whole.db"),config).AppendAsync([headshotEvent],default)).Single()==headshotReceipt,"kill opponent and headshot receipt survive restart and retry");
    var custom=config with {Messages=new() {["ScoreKill"]="+{amount}: {opponent}",["ScoreHeadshotKill"]="HS +{amount}: {opponent}"}};custom.Validate();
    Check(ScoreReceiptFilter.Message(headshotReceipt with { KillsAfter = 3 },custom)=="HS +7: Bot","kill templates remain configurable");
    var tiePolicy=config with {Ranking=config.Ranking with {KillBase=5.5,DeathBase=3.5}};
    var ties=new LiteStore(Path.Combine(directory,"ties.db"),tiePolicy);
    var tieEvent=normal with {EventId=Guid.NewGuid(),Data=((KillEvent)normal.Data) with {Victim=bob}};
    var tieReceipts=await ties.AppendAsync([tieEvent],default);
    Check(tieReceipts.Single(r=>r.Reason=="kill").Delta==6 && tieReceipts.Single(r=>r.Reason=="death").Delta==-4,"positive and negative halves round away from zero before booking");
    var legacyPolicy=config with {Ranking=config.Ranking with {RoundPointsToIntegers=false,KillBase=5.5}};
    var legacyPath=Path.Combine(directory,"legacy.db");
    var legacy=new LiteStore(legacyPath,legacyPolicy);
    var legacyReceipt=(await legacy.AppendAsync([normal],default)).Single();
    Check(legacyReceipt.Delta==5.5 && legacyReceipt.PointsAfter==1005.5,"fractional mode remains explicitly available");
    var upgraded=new LiteStore(legacyPath,config);
    Check((await Profile(upgraded,alice)).GetProperty("points").GetDouble()==1006,"legacy balance normalized on enabling whole-point mode");
    Check((await upgraded.AppendAsync([normal],default)).Single()==legacyReceipt,"normalization preserves historical receipt amounts");
    var newReceipt=(await upgraded.AppendAsync([Kill()],default)).Single();
    Check(newReceipt.PointsAfter-newReceipt.Delta==1006 && newReceipt.Delta==Math.Truncate(newReceipt.Delta),"new receipt delta equals actual credited integer balance difference");
    var smallFloor=config with {Ranking=config.Ranking with {StartingPoints=1,DeathBase=5.5}};
    var floored=await new LiteStore(Path.Combine(directory,"whole-floor.db"),smallFloor).AppendAsync([tieEvent],default);
    Check(floored.Single(r=>r.Reason=="death").Delta==-1 && floored.Single(r=>r.Reason=="death").PointsAfter==0,"rounded penalty receipt reports the actual floor-limited amount");
    try {(config with {Ranking=config.Ranking with {MinimumPoints=.5}}).Validate();throw new Exception("Expected invalid floor");}catch(InvalidOperationException){Check(true,"whole-point mode rejects fractional floor");}
    var timeStore=new LiteStore(Path.Combine(directory,"time.db"),config);
    long clock=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var old=alice with {SessionId="old-time"};var active=alice with {SessionId="active-time"};var stale=alice with {SessionId="stale-time"};
    await timeStore.AppendAsync([
        Event("session_start",new PlayerEvent(old,clock-200000000,"connect")) with {Timestamp=clock-200000000},
        Event("session_end",new PlayerEvent(old,clock-200000000,"disconnect")) with {Timestamp=clock-200000000+90060000},
        Event("session_start",new PlayerEvent(active,clock-3660000,"connect")) with {Timestamp=clock-3660000},
        Event("session_start",new PlayerEvent(stale,clock-400000000,"connect")) with {Timestamp=clock-400000000},
        Event("shot",new ShotEvent(stale,"ak47",false)) with {Timestamp=clock-400000000+120000}
    ],default);
    var timed=(await timeStore.ReadAsync($"players/{alice.Steamid}?active_session={active.SessionId}&active_collector={collector}",default))!.Value;
    var elapsed=timed.GetProperty("playtime_seconds").GetDouble();
    Check(elapsed>=93840 && elapsed<93845,"playtime combines closed sessions current live time and bounded stale observations");
    using var timeCommands=new StatsCommandClient(config,timeStore);
    var timeIdentity=new CommandPlayer(alice.Steamid!,active.SessionId,collector);
    var sessionLines=await timeCommands.Execute("session",[],timeIdentity,default);
    var profileLines=await timeCommands.Execute("statsme",[],timeIdentity,default);
    Check(ChatColors.Plain(sessionLines[1])=="Playtime: 1 hour 1 minute","session renders live playtime");
    Check(ChatColors.Plain(profileLines[^1]).Contains("Playtime: 1 day 2 hours 4 minutes"),"profile renders cumulative playtime");
    Check(ScoreReceiptFilter.Message(headshotReceipt with { KillsAfter = 3 },config).EndsWith("[green](1012)[/]"),"total points use the same green and parentheses");
    Console.WriteLine($"{checks} checks passed against real SQLite.");
}
finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory,true); }
