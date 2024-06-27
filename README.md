# AdemirBot

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://github.com/welldtr/AdemirBot/actions/workflows/dotnet.yml/badge.svg)](https://github.com/welldtr/AdemirBot/actions/workflows/dotnet.yml)
[![Docker Image CI](https://github.com/welldtr/AdemirBot/actions/workflows/docker-image.yml/badge.svg)](https://github.com/welldtr/AdemirBot/actions/workflows/docker-image.yml)
[![Build Status](https://dev.azure.com/ademirbot/AdemirBot/_apis/build/status%2Fwelldtr.AdemirBot?branchName=production)](https://dev.azure.com/ademirbot/AdemirBot/_build/latest?definitionId=1&branchName=production)
[![SonarCloud](https://github.com/welldtr/AdemirBot/actions/workflows/sonarcloud.yml/badge.svg)](https://github.com/welldtr/AdemirBot/actions/workflows/sonarcloud.yml)

## Descrição
O projeto "Ademir" é um bot criado para melhorar a experiência de comunidades focadas em bem-estar. Ele permite reproduzir músicas, contabilização de XP, criar macros, efetuar ações de moderação em massa e conversar com a API do ChatGPT.

## Funcionalidades
- :white_check_mark: Falar com o bot apenas mencionando o mesmo
- :white_check_mark: Reprodução/download de músicas/playlists em canal de audio
- :white_check_mark: Suporte a links de vídeo do YouTube
- :white_check_mark: Suporte a links de musicas do Spotify
- :white_check_mark: Suporte a links de albuns do Spotify
- :white_check_mark: Suporte a links de playlists públicas do Spotify
- :white_check_mark: Suporte a links de playlists públicas do YouTube
- :white_check_mark: Suporte a contabilização de eventos participados
- :white_check_mark: Suporte a contabilização de bumps efetuados
- :white_check_mark: Suporte a contabilização de XP por envio de mensagens
- :white_check_mark: Suporte a proteção de flood
- :white_check_mark: Suporte a blacklist de padrão de mensagens
- :white_check_mark: Suporte a auto banimento de menores de idade por cargo
- :white_check_mark: Suporte a contabilização de XP por tempo de call
- :white_check_mark: Suporte a contabilização de XP por bump no server
- :white_check_mark: Multiplicador de XP ao participar de eventos
- :white_check_mark: Multiplicador de XP por canal de texto
- :white_check_mark: Importação de informações de level de outro bot (Lurkr)
- :white_check_mark: Listar os comandos por módulo com `/help`
- :white_check_mark: Obter card de XP através do comando `/rank`
- :white_check_mark: Denunciar um usuário através do comando `/denunciar`
- :white_check_mark: Visualizar avatar com o comando `/avatar`
- :white_check_mark: Visualizar banner com o comando `/banner`
- :white_check_mark: Definir a cor principal do card de xp com `/colour`
- :white_check_mark: Definir o fundo do card de xp com `/background`
- :white_check_mark: Sincronizar recompensas de nível com `/syncrolerewards`
- :white_check_mark: Visualizar ranking com `/leaderboard`
- :white_check_mark: Visualizar contagem de membros com `/membercount`
- :white_check_mark: Visualizar evolução de membros com `/membergraph`
- :white_check_mark: Visualizar o delta diário de membros com `/growthchange`
- :white_check_mark: Prever quando o server vai ter n membros com `/predict`
- :white_check_mark: Verificar memória do servidor com `/memory`
- :white_check_mark: Criar um hash MD5 de um texto com `/md5`
- :white_check_mark: Criar um GUID com `/guid`
- :white_check_mark: Denunciar uma mensagem com o menu de contexto

## Comandos de Booster
- :white_check_mark: Falar com o bot em uma thread com o comando `/thread`
- :white_check_mark: Reiniciar a sua thread com o Ademir com `/restart-thread`
- :white_check_mark: Gerar imagens com o comando `/dall-e`
- :white_check_mark: Gerar texto com o comando `/completar`

## Comandos do Administrador
- :white_check_mark: Configurar o Canal de Denúncias: Comando `/config-denuncias`
- :white_check_mark: Criar macros através do comando `/macro`
- :white_check_mark: Listar macros: comando `/listar-macros`
- :white_check_mark: Editar macros: comando `/editar-macro`
- :white_check_mark: Excluir macro: comando `/excluir-macro`
- :white_check_mark: Banir: comando `/ban`
- :white_check_mark: Expulsar: comando `/kick`
- :white_check_mark: Banir em massa: comando `/massban`
- :white_check_mark: Expulsar em massa: comando `/masskick`
- :white_check_mark: Bloquear a chegada de novos membros: comando `/lock-server`
- :white_check_mark: Desloquear a chegada de novos membros: comando `/unlock-server`
- :white_check_mark: Habilitar módulo de cargos: comando `/togglerolerewards`
- :white_check_mark: Importar levels de outro bot (Lurkr): comando `/importlevelinfo`
- :white_check_mark: Definir XP de um membro: comando `/xp set`
- :white_check_mark: Adicionar XP a um membro: comando `/xp add`
- :white_check_mark: Remover XP de um membro: comando `/xp remove`
- :white_check_mark: Remover uma certa quantidade de mensagens de um canal: comando `/purge`
- :white_check_mark: Importar histórico de mensagens: comando `/importar-historico-mensagens`
- :white_check_mark: Configurar cargo de participação ativa: comando `/set-activetalker-role`
- :white_check_mark: Configurar cargo de convite para eventos: comando `/set-eventinvite-role`
- :white_check_mark: Define o canal de eventos de voz padrão: comando `/set-voice-event-channel`
- :white_check_mark: Define o canal de eventos de palco padrão: comando `/set-stage-event-channel`
- :white_check_mark: Extrair lista de usuarios por atividade no servidor `/usuarios-inativos`
- :white_check_mark: Configurar cargo extra para falar com o bot: comando `/config-cargo-ademir`

### Comandos de Mensagem: 
- Criar Evento de Voz: Cria um evento de voz no servidor a partir da mensagem (reconhece data/hora)
- Criar Evento Palco: Cria um evento de palco no servidor a partir da mensagem (reconhece data/hora)
- Denunciar: Denuncia uma mensagem para a staff
- Blacklist: Coloca o padrão da mensagem na lista negra (para apagar)

## Comandos de Música
- :white_check_mark: `/play <link/track/playlist/album/artista>`: Reproduz uma música, playlist, artista ou álbum.
- :white_check_mark: `/skip`: Pula para a próxima música da fila.
- :white_check_mark: `/back`: Pula para a música anterior da fila.
- :white_check_mark: `/replay`: Reinicia a música atual.
- :white_check_mark: `/pause`: Pausa/Retoma a reprodução da música atual.
- :white_check_mark: `/stop`: Interrompe completamente a reprodução de música.
- :white_check_mark: `/remove member <membro>`: Remove as músicas de um membro da playlist.
- :white_check_mark: `/remove index <posicao>`: Remove uma musica da playlist na posição fornecida.
- :white_check_mark: `/remove range <inicio> <fim>`: Remove musicas de playlist no intervalor de inicio e fim.
- :white_check_mark: `/remove last`: Remove a última música da playlist.
- :white_check_mark: `/loop`: Habilita/Desabilita o modo de repetição de faixa.
- :white_check_mark: `/loopqueue`: Habilita/Desabilita o modo de repetição de playlist.
- :white_check_mark: `/queue`: Mostra a lista de reprodução.
- :white_check_mark: `/join`: Puxa o bot para o seu canal de voz.
- :white_check_mark: `/quit`: Remove o bot da chamada de voz.
- :white_check_mark: `/volume <valor>`: Ajusta o volume da música.

## Instalação (DevEnv)

### Dependências externas
Para utilizar todos os recursos desenvolvidos nesse projeto é necessário:
1. Criar um aplicativo no [Developer Portal do Discord](https://discord.com/developers/docs/getting-started)
2. Criar um aplicativo no [Developer Console do Spotify](https://developer.spotify.com/documentation/web-api/tutorials/getting-started)
3. Criar uma conta (paga) no OpenAI e criar uma [API Key](https://platform.openai.com/account/api-keys)
4. Criar uma instância MongoDB para o Bot guardar os dados de configuração

### Passo a passo
Para utilizar o bot "Ademir" em seu servidor do Discord, siga as etapas abaixo:
1. Clone este repositório em sua máquina local.
2. Instale as dependências necessárias executando o comando `pip install -r requirements.txt`.
3. Defina as seguintes varáveis de ambiente:
   - `SpotifyApiClientId`: Client ID do Aplicativo Spotify.
   - `SpotifyApiClientSecret`: Client Secret do Aplicativo Spotify
   - `PremiumGuilds`: IDs dos Servers permitidos para utilizar o ChatGPT
   - `AdemirAuth`: Token de autenticação do bot do Discord
   - `MongoServer`: String de conexão do Mongo DB
   - `ChatGPTKey`: Token de autenticação da conta de API do ChatGPT
4. Execute o bot utilizando o comando `python main.py`.

## Instalação (Docker)
Rode o seguintes comandos para iniciar o Ademir no docker:

Para construir a imagem:
```sh
docker build -t ademir .
```

Para iniciar o container:
```sh
docker run -e SpotifyApiClientId=<Client ID do Aplicativo Spotify> \
           -e SpotifyApiClientSecret=<Client Secret do Aplicativo Spotify> \
           -e PremiumGuilds=<IDs dos Servers permitidos para utilizar o ChatGPT> \
           -e AdemirAuth=<Token de autenticação do bot do Discord> \
           -e MongoServer=<String de conexão do Mongo DB> \
           -e ChatGPTKey=<Token de autenticação da conta de API do ChatGPT> \
           ademir
```

## Licença
Este projeto está licenciado sob a licença MIT. Consulte o arquivo [LICENSE.txt](LICENSE.txt) para obter mais informações.

## Contato
Se você tiver alguma dúvida ou sugestão sobre o projeto "Ademir", sinta-se à vontade para entrar em contato:
- [Discord](https://disboard.org/server/1254275584243007559)
