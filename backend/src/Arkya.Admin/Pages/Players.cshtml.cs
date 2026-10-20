using Arkya.Admin.Accounts;

namespace Arkya.Admin.Pages;

public sealed class PlayersModel(CharactersAdminClient characters) : CharacterDirectoryModel(characters, true);
