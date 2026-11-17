import {legacy,character} from '../generated/validators.mjs';
// Only the two upstream avatar schemas enter this bundled runtime. Unknown schemas fail closed.
export default class PrecompiledAvatarValidator {
  compile(schema){
    if(schema.$id==='https://avatars.bible-strong.app/schema/avatar-character.json')return character;
    if(schema.$id==='https://avatars.bible-strong.app/schemas/avatar-definition-v1.json')return legacy;
    throw new Error('Unsupported avatar schema');
  }
}
