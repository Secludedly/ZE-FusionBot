<h1 align="center">
    <a href="https://amplication.com/#gh-light-mode-only">
    <img src="https://i.imgur.com/43WJlCN.png">
    </a>
    <a href="https://amplication.com/#gh-dark-mode-only">
    <img src="https://i.imgur.com/43WJlCN.png">
    </a>
</h1>

<p align="center">
  <i align="center">A community-driven and inspired SysBot.NET project, uniting code from everyone!</i>
</p>

<h4 align="center">
  <a href="https://FreeMons.Org">
    <img src="https://i.imgur.com/wbWzI2u.png" alt="discord" style="height: 20px;">
  </a>
  <a href="https://ko-fi.com/secludedly">
    <img src="https://i.imgur.com/nDO4SgL.png" alt="ko-fi" style="height: 20px;">
  </a>
</h4>



<p align="center">
    <img src="https://raw.githubusercontent.com/Secludedly/ZE-FusionBot/main/.readme/hub.gif">
</p>
<p align="center">
    <img src="https://raw.githubusercontent.com/Secludedly/ZE-FusionBot/main/.readme/modes.gif">
</p>
<p align="center">
    <img src="https://raw.githubusercontent.com/Secludedly/ZE-FusionBot/main/.readme/themes.gif">
</p>


## Introduction

`ZE FusionBot` is a robust, open-source SysBot.NET fork originally meant specifically for the Discord server, Zeraora's Emporium, hence the 'ZE' in the name. It was originally meant to satisfy the personal needs of the bot owners running exclusively in the server, but it slowly evolved into the mess that it is today.

This bot unites the forks of various other developers - some new, some old, some advanced, some only beginning, and it adds to it, making it now a massively combined community program. It started out humble, but now packs a plethora of features. It's evolved to where it's mostly based on [Gengar's](https://github.com/bdawg1989) [MergeBot](https://github.com/bdawg1989/MergeBot) project.

**Some features include:**
- *Restart button to reboot the game and load back in to trade with the bot again.*
- *Update button to keep up-to-date with my personal releases.*
- *Choose your own permanent Link Trade Code for all trades.*
- *Batch trading, allowing up to 6 trades at once per person.*
- *Trade via PokePaste URLs.*
- *DM embeds for easier and less cluttered bot messages, with cute little gifs attached.*
- *Hidden Trades, where only the species is shown. Sets and files get deleted instantly.*
- *Custom text-only option in case embeds aren't your thing, but still has some flavor.*
- *Generate and trade random VGC teams, or rent them in-game with a code.*
- *Battle-Ready competitive Pokemon trade module, for competitive players.*
- *HOME-Ready trade module to select and trade Pokemon with HOME trackers.*
- *SysDVR integration, to view your Switch live on your PC.*
- *SysBotRemote integration, allowing a GUI controller on PC to control your Switch.*
- *Event trade module to search and trade event/gift Pokemon.*
- *Mystery Egg module to generate an egg for all games with complete randomness.*
- *Auto-Correct, which corrects misspellings and illegal formats to automatically become legal.*
- *Custom UI themes, with more to soon be added.*
- *Drop-down menu to select which game mode you want to use.*
- *Bot Start & Stop embed that keeps bot channels active while displaying the bot's online status.*
- *Announcement module, to send messages to multiple channels and servers at once.*
- *Egg trade support for supported games.*
- *AutoOT, which applies your game's trainer info automatically, unless otherwise specified.*
- *Supports Let's Go, Pikachu & Let's Go, Eevee.*
- *Server Blacklist, to ban a server from using your bot.*
- *Send messages using your bot as the speaker.*
- *Ability to keep track of how many times a user traded with the bots.*




<details open>
<summary>
 Images
</summary> <br />

<p align="center">
    <img width="49%" src="https://i.imgur.com/yLYCuAj.png" alt="img1"/>
&nbsp;
    <img width="49%" src="https://i.imgur.com/ShpbwW5.png" alt="img2"/>
</p>
<p align="center">
    <img width="49%" src="https://i.imgur.com/7qhL9Ys.png" alt="img3"/>
&nbsp;
    <img width="49%" src="https://i.imgur.com/N4QS3e8.png" alt="img4"/>
</p>
<p align="center">
    <img width="80%" src="https://i.imgur.com/mHvBUcL.png" alt="img5"/>
</p>
</details>



<details open>
<summary>
 GIFS
</summary> <br />

<p align="center">
    <img width="49%" src="https://raw.githubusercontent.com/Secludedly/ZE-FusionBot/main/.readme/sbremotestart.gif" alt="gif1"/>
&nbsp;
    <img width="49%" src="https://raw.githubusercontent.com/Secludedly/ZE-FusionBot/main/.readme/sdvrstart.gif" alt="gif2"/>
</p>
<p align="center">
    <img width="49%" src="https://raw.githubusercontent.com/Secludedly/ZE-FusionBot/main/.readme/batch.gif" alt="gif3"/>
&nbsp;
    <img width="49%" src="https://raw.githubusercontent.com/Secludedly/ZE-FusionBot/main/.readme/batch2.gif" alt="gif4"/>
</p>
<p align="center">
    <img width="80%" src="https://raw.githubusercontent.com/Secludedly/ZE-FusionBot/main/.readme/video.gif" alt="gif5"/>
</p>
</details>



## BASIC USE COMMANDS
`trade = Initiate a Link Trade.`
`hidetrade = Initiate a Link Trade without displaying your Pokemon's embed information in the channel.`
`tradeuser <Ping User> = Trades a PKM file to a mentioned user.`
`clone = Initiate a Clone Trade.`
`dump = The bot DMs you PKM files of the Pokemon you show it.`
`seed = Check for seeds in supported games.`
`egg <Showdown Format> = Trade for an egg.`
`it <Held Item> = Trade an item.`
`fixot = Scrubs the URL from an admon's nickname and sends you a clean copy.`
`convert = Convert Showdown Format to PKM file, legalizing a Pokemon set in the process.`
`lcv = Check if a PKM file is legal.`
`legalize = Attempts to legalize a PKM file.`

## BATCH TRADING
`btz = Place up to 6 files into a .zip archive and trade it.`
```c#
bt
Showdown Template
---
Showdown Template
---
Showdown Template
```

## STATUS COMMANDS
`status = Current bot status.`
`info = Info about the bot.`
`help = Brings up the full list of bot options, with descriptions.`

## TRADE MANAGEMENT
`dtc = Delete current Link Trade Code.`
`atc <00000000> = Assign a new, personal use Link Trade Code.`
`qc = Removes you from a queue.`
`qs = Checks position in the queue.`

## ENHANCED TRADE FEATURES
`me = Get a mystery egg! All shiny, 6IV, and have their Hidden Ability.`
`brl <species name> <page number> = List all pre-made Battle-Ready Pokemon for trade.`
`le <species name> <page number> = List all Event Pokemon for trade.`
`pp <URL> = Gen a Pokemon team from a PokePaste URL.`
`hr = View, trade, and download legal HOME-Ready files for transfer.`
`rt = DMs you a zip file of a random VGC team, with info about it.`
- You can trade the actual zip file with the btz command.
`srp <game> <page> = The user will obtain a list of valid events for each game.`
- srp commands: gen9, bdsp, pla, swsh, gen7, gen6, gen5, gen4, gen3.
`dt <LinkCode> <IVToBe0> <Language> <Nature> = Trades you a Ditto.`
- Example: $dt 22222222 ATK Japanese Timid

## BOT MANAGEMENT
`kill = Terminates the entire bot.`
`previoususersummary = Prints a list of previously encountered users.`
`forgetuser <ID> = Forgets users that were previously encountered.`
`ql = Lists everyone in a queue.`
`tl = Lists everyone in the Trade queue.`
`cl = Lists everyone in the Clone queue.`
`dl = Lists everyone in the Dump queue.`
`fl = Lists everyone in the FixOT queue.`
`sl = Lists everyone in the Seed queue.`

## SWITCH MANAGEMENT
`screenon = Turns your Switch screen on.`
`screenoff = Turns your Switch screen off.`
`sysdvr = Opens SysDVR to view live video of your Switch, with setup instructions.`
`sbr = Opens SysBotRemote, a GUI that emulates buttons presses on the Switch, with instructions.`
`video = Shows a video gif of your current Switch screen.`
`peek = Shows an image of your current Switch screen.`

## PERMISSIONS
`blacklistid <ID> = Blaclists a specified user in or out of your server.`
`unblacklistid <ID> = Removes blacklisting of a specified user in or out of your server.`
`blacklistserver <ID> = Blacklists a specified server from using the bot.`
`unblacklistserver <ID> = Removes blacklisting of a specific server.`
`banid <ID> = Bans a specified user.`
`unbanid <ID> = Unbans a specified user.`

## MISC FEATURES
`setavatar = Sets the bot's avatar to an animated GIF.`
`hi = Say hi to the bot and it'll respond with something the bot owner's can customize.`
`joke = Tells a random joke. It's pointless and silly. Probably not even funny.`
`say = Sends a message as the bot to a channel.`
`dm = Sends a message as the bot to a user's DM.`
`ping = Makes the bot respond, indicating that it is running.`