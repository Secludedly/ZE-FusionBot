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
    <img src="https://i.imgur.com/vvaZFdG.gif" alt="dashboard1"/>
</p>
<p align="center">
    <img src="https://i.imgur.com/tZ10x1h.gif" alt="dashboard2"/>
</p>
<p align="center">
    <img src="https://i.imgur.com/axDpP2X.gif" alt="dashboard3"/>
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
    <img width="49%" src="https://i.imgur.com/yLYCuAj.png" alt="apis"/>
&nbsp;
    <img width="49%" src="https://i.imgur.com/ShpbwW5.png" alt="data-models"/>
</p>
</p>

</details>

## Usage 

**trade** = Initiate a Link Trade.

**hidetrade** = Initiate a Link Trade without displaying your Pokemon's embed information in the channel.

**clone** = Initiate a Clone Trade.

**convert** = Convert Showdown Format to PKM file, legalizing a Pokemon set.

**dump** = The bot DMs you PKM files of the Pokemon you show it.

**seed** = Check for seeds in supported games.

**lcv** = Check if a PKM file is legal.

**legalize** = Attempts to legalize a PKM file.

**qc** = Removes you from a queue.

**qs** = Checks position in the queue.

**egg <Showdown Format>** = Trade for an egg.

**it <Held Item>** = Trade an item.

**fixot** = Scrubs the URL from an admon's nickname and sends you a clean copy.

**tradeuser** = Trades a file to a mentioned user.

**status** = Current generalized bot status.

**info** = Info about the bot.

**me** = Get a mystery egg! All shiny, 6IV, and have their HA.

**brl <species name> <page number>** = List all pre-made Battle-Ready Pokemon for trade.

**le <species name> <page number>** = List all Event Pokemon for trade.

**pokepaste <URL>** = Gen a Pokemon team from a PokePaste URL.

**rt** = DMs you a zip file of a random VGC team, with info about it.
- *You can trade the actual zip file with $btz.*

**srp <game> <page>** = The user will obtain a list of valid events for each game.
- *srp commands: gen9, bdsp, pla, swsh, gen7, gen6, gen5, gen4, gen3.*

**dt <LinkCode> <IVToBe0> <Language> <Nature>** = Trades you a Ditto.
- *Example: $dt 22222222 ATK Japanese Timid*

**hr** = View, trade, and download legal HOME-Ready files for transfer.

## Batch Trading

**bt**
**Showdown Template**
---
**Showdown Template**
---
**Showdown Template**

**btz** = Place up to 6 files into a .zip archive and trade it.