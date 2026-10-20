using Arkya.Admin.Accounts;

namespace Arkya.Admin.Pages;

public sealed class CharactersModel(CharactersAdminClient characters) : CharacterDirectoryModel(characters, false);
