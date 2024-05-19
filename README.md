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
    <img width="65%" src="https://raw.githubusercontent.com/Secludedly/ZE-FusionBot/main/.readme/video.gif" alt="gif5"/>
</p>
</details>



## BASIC USE COMMANDS
`trade` // Initiate a Link Trade. <br />
`hidetrade` // Initiate a Link Trade without displaying your Pokemon's embed information in the channel. <br />
`clone` // Initiate a Clone Trade. <br />
`dump` // The bot DMs you PKM files of the Pokemon you show it. <br />
`seed` // Check for seeds in supported games. <br />
`fixot` // Scrubs the URL from an admon's nickname and sends you a clean copy. <br />
`lcv` // Check if a PKM file is legal. <br />
`legalize` // Attempts to legalize a PKM file. <br />
`convert <Showdown Format>` // Convert Showdown Format to PKM file, legalizing a Pokemon set in the process. <br />
`tradeuser <Ping User>` // Trades a PKM file to a mentioned user. <br />
`egg <Showdown Format>` // Trade for an egg. <br />
`it <Held Item>` // Trade an item. <br />

## BATCH TRADING
`btz` // Place up to 6 files into a .zip archive and trade it. <br />
```c#
bt
Showdown Template
---
Showdown Template
---
Showdown Template
```

## STATUS COMMANDS
`status` // Current bot status. <br />
`info` // Info about the bot. <br />
`help` // Brings up the full list of bot options, with descriptions. <br />

## TRADE MANAGEMENT
`dtc` // Delete current Link Trade Code. <br />
`atc <00000000>` // Assign a new, personal use Link Trade Code. <br />
`qc` // Removes you from a queue. <br />
`qs` // Checks position in the queue. <br />

## ENHANCED TRADE FEATURES
`me` // Get a mystery egg! All shiny, 6IV, and have their Hidden Ability. <br />
`brl <species name> <page number>` // List all pre-made Battle-Ready Pokemon for trade. <br />
`le <species name> <page number>` // List all Event Pokemon for trade. <br />
`pp <URL>` // Gen a Pokemon team from a PokePaste URL. <br />
`hr` // View, trade, and download legal HOME-Ready files for transfer. <br />
`rt` // DMs you a zip file of a random VGC team, with info about it. <br />
— *You can trade the actual zip file with the btz command.* <br />
`srp <game> <page>` // The user will obtain a list of valid events for each game. <br />
— **srp commands:** *gen9, bdsp, pla, swsh, gen7, gen6, gen5, gen4, gen3.* <br />
`dt <LinkCode> <IVToBe0> <Language> <Nature>` // Trades you a Ditto. <br />
— **Example:** *dt 22222222 ATK Japanese Timid.* <br />

## BOT MANAGEMENT
`kill` // Terminates the entire bot. <br />
`previoususersummary` // Prints a list of previously encountered users. <br />
`forgetuser <ID>` // Forgets users that were previously encountered. <br />
`ql` // Lists everyone in a queue. <br />
`tl` // Lists everyone in the Trade queue. <br />
`cl` // Lists everyone in the Clone queue. <br />
`dl` // Lists everyone in the Dump queue. <br />
`fl` // Lists everyone in the FixOT queue. <br />
`sl` // Lists everyone in the Seed queue. <br />

## SWITCH MANAGEMENT
`screenon` // Turns your Switch screen on. <br />
`screenoff` // Turns your Switch screen off. <br />
`sysdvr` // Opens SysDVR to view live video of your Switch, with setup instructions. <br />
`sbr` // Opens SysBotRemote, a GUI that emulates buttons presses on the Switch, with instructions. <br />
`video` // Shows a video gif of your current Switch screen. <br />
`peek` // Shows an image of your current Switch screen. <br />

## PERMISSIONS
`blacklistid <ID>` // Blaclists a specified user in or out of your server. <br />
`unblacklistid <ID>` // Removes blacklisting of a specified user in or out of your server. <br />
`blacklistserver <ID>` // Blacklists a specified server from using the bot. <br />
`unblacklistserver <ID>` // Removes blacklisting of a specific server. <br />
`banid <ID>` // Bans a specified user. <br />
`unbanid <ID>` // Unbans a specified user. <br />

## MISC FEATURES
`setavatar` // Sets the bot's avatar to an animated GIF. <br />
`hi` // Say hi to the bot and it'll respond with something the bot owner's can customize. <br />
`joke` // Tells a random joke. It's pointless and silly. Probably not even funny. <br />
`say` // Sends a message as the bot to a channel. <br />
`dm` // Sends a message as the bot to a user's DM. <br />
`ping` // Makes the bot respond, indicating that it is running. <br />