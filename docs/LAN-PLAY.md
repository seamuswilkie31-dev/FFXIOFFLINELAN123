# Playing with friends

One person **hosts**: their PC runs the server and holds everyone's characters. Everyone else **joins**.
The host's PC must be on, with the server running, for others to play.

## Pick how you'll connect

| Situation | Use |
|---|---|
| Everyone in the same house / on the same Wi-Fi | Your home network address (192.168.x.x or 10.x.x.x) |
| Friends elsewhere | A gaming VPN: [Radmin VPN](https://www.radmin-vpn.com) (free, easiest), Hamachi, ZeroTier or Tailscale |

The host picks **one** address and everyone uses it. If some friends are in the house and some are remote, put
everyone on Radmin VPN, including the people in the house.

LocalXI never asks you to open ports on your router, and its firewall rules only accept home-network and VPN
addresses.

## Host

1. *(Remote friends)* Install Radmin VPN, choose **Network → Create network**, and give friends the network
   name and password.
2. In LocalXI, open **Network**, choose **Host for friends**, pick your **Radmin VPN - 26.x.x.x** address (or
   your home network address) and press **Apply**.
3. Press **Allow friends through Windows Firewall** and accept the Windows prompt. This is only needed once.
4. Press **Play**. The Play page shows the address to send your friends.

When you close your game, the server keeps running if friends are still online. Stop it from the Play page
when everyone is done (closing LocalXI also stops it, with a backup).

**Accounts:** friends' launchers create their accounts on your server automatically. The Network page lists
accounts and lets you create one for a friend or reset a forgotten password.

**Radmin addresses can change** if you reinstall Radmin. If friends suddenly can't connect, check the Network page.

## Join

1. *(Remote)* Install Radmin VPN and **Join network** using the name and password from your host.
2. In LocalXI choose **Join a friend** (in setup, or on the Network page) and enter the host's address.
   **Test connection** checks you can reach their server.
3. Pick a username and password. LocalXI creates the account on your friend's server.
4. Press **Play** while your friend's server is running.

Your characters live on the host's PC. To keep a copy, ask the host to export a save.

## If it doesn't connect

- The host's server must be running (they pressed Play, or started it).
- You must both be **online** in the same Radmin network. Radmin shows a green dot next to each person.
- The host must have allowed LocalXI through the firewall (Network page).
- Third-party antivirus firewalls (Norton, McAfee, Bitdefender...) may block the server even after Windows
  Firewall allows it. Allow `xi_connect`, `xi_search`, `xi_map`, `xi_world` and `xi_profile` from
  `%LOCALAPPDATA%\LocalXI\runtime\server`.
- Everyone needs the same, up-to-date version of the game.

## Ports (for reference)

| Port | Program |
|---|---|
| TCP 54001, 54230, 54231 | `xi_connect` (login) |
| TCP 54002 | `xi_search` |
| TCP 51220 | `xi_profile` |
| UDP 54230 | `xi_map` (the game world) |
